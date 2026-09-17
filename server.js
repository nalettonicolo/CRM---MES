const express = require('express');
const fs = require('fs');
const path = require('path');
const multer = require('multer');
const pdfParse = require('pdf-parse');
const mammoth = require('mammoth');
const XLSX = require('xlsx');
const cors = require('cors');

const app = express();
const PORT = process.env.PORT || 3000;
const DATA_FILE = path.join(__dirname, 'data', 'store.json');
const TMP_DIR = path.join(__dirname, 'tmp');
const MECHANICAL_EXTENSIONS = new Set([
  'ipt', 'iam', 'ipn', 'idw', 'dwg', 'dxf', 'step', 'stp', 'sldprt', 'sldasm', 'prt', 'asm', 'iges', 'igs'
]);

fs.mkdirSync(TMP_DIR, { recursive: true });

const storage = multer.diskStorage({
  destination: (_, __, cb) => cb(null, TMP_DIR),
  filename: (_, file, cb) => cb(null, `${Date.now()}-${file.originalname.replace(/\s+/g, '-')}`)
});
const upload = multer({ storage });

function makeId(prefix) {
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}

function getSeedData() {
  return {
    areas: [
      { id: 'AREA-1', name: 'Produzione', code: 'PROD', buyerOffice: 'Ufficio Acquisti' },
      { id: 'AREA-2', name: 'Montaggio', code: 'MNT', buyerOffice: 'Ufficio Acquisti' },
      { id: 'AREA-3', name: 'Magazzino', code: 'MAG', buyerOffice: 'Ufficio Acquisti' }
    ],
    users: [
      { id: 'USER-1', name: 'Marco Rossi', email: 'marco.rossi@azienda.it', role: 'operatore', areaIds: ['AREA-1'] },
      { id: 'USER-2', name: 'Laura Bianchi', email: 'laura.bianchi@azienda.it', role: 'responsabile', areaIds: ['AREA-2'] },
      { id: 'USER-3', name: 'Ufficio Acquisti', email: 'acquisti@azienda.it', role: 'acquisti', areaIds: ['AREA-1', 'AREA-2', 'AREA-3'] }
    ],
    materials: [
      { id: 'MAT-1', code: 'MAT-100', description: 'Cavo 3x1.5 mm²', stock: 120 },
      { id: 'MAT-2', code: 'MAT-200', description: 'Terminale rapido', stock: 85 },
      { id: 'MAT-3', code: 'MAT-300', description: 'Quadro di comando', stock: 40 }
    ],
    suppliers: [
      { id: 'SUP-1', name: 'Schneider Electric', code: 'SCH', website: 'https://www.se.com/it/it/' },
      { id: 'SUP-2', name: 'Pizzato', code: 'PIZ', website: 'https://www.pizzato.com/' }
    ],
    supplierCatalog: [
      { id: 'CAT-1', supplier: 'Schneider Electric', code: 'A9N', name: 'Interruttore magnetotermico', description: 'MCCB per quadro', family: 'Protezione', uom: 'pz', price: 28.5 },
      { id: 'CAT-2', supplier: 'Schneider Electric', code: 'GV2ME', name: 'Contattore', description: 'Contattore 3 poli', family: 'Controllo', uom: 'pz', price: 42.0 },
      { id: 'CAT-3', supplier: 'Schneider Electric', code: 'GV3P', name: 'Relè termico', description: 'Relè termico per contattore', family: 'Protezione', uom: 'pz', price: 19.4 },
      { id: 'CAT-4', supplier: 'Schneider Electric', code: 'NSX400', name: 'Modulo quadro automatico', description: 'Modulo quadro automatico', family: 'Quadri', uom: 'pz', price: 180.0 },
      { id: 'CAT-5', supplier: 'Pizzato', code: 'PZ-101', name: 'Terminale a vite', description: 'Terminale rapido', family: 'Connessioni', uom: 'pz', price: 1.2 },
      { id: 'CAT-6', supplier: 'Pizzato', code: 'PZ-220', name: 'Supporto guida cavo', description: 'Supporto staffa', family: 'Accessori', uom: 'pz', price: 3.8 },
      { id: 'CAT-7', supplier: 'Pizzato', code: 'PZ-330', name: 'Barra di terra', description: 'Barra di terra din', family: 'Distribuzione', uom: 'pz', price: 11.4 },
      { id: 'CAT-8', supplier: 'Pizzato', code: 'PZ-440', name: 'Cassettone supporto', description: 'Supporto cassettone', family: 'Accessori', uom: 'pz', price: 5.6 }
    ],
    rules: [
      { id: 'RULE-1', name: 'Codice non presente', description: 'Se il materiale non è presente in catalogo, avvisa l’ufficio acquisti e genera una richiesta d’ordine.', severity: 'high', action: 'notify_buyer' },
      { id: 'RULE-2', name: 'Area riservata', description: 'Ogni utente può operare solo sulle aree assegnate.', severity: 'medium', action: 'restrict_area' },
      { id: 'RULE-3', name: 'Conferma ordine', description: 'Se il materiale è mancante e l’utente è autorizzato, viene generata una conferma d’ordine fornitore.', severity: 'high', action: 'create_purchase_order' }
    ],
    withdrawals: [],
    alerts: [],
    purchaseOrders: [],
    events: []
  };
}

function ensureDataFile() {
  const fileExists = fs.existsSync(DATA_FILE);
  if (!fileExists) {
    fs.mkdirSync(path.dirname(DATA_FILE), { recursive: true });
    fs.writeFileSync(DATA_FILE, JSON.stringify(getSeedData(), null, 2));
    return;
  }

  try {
    const current = fs.readFileSync(DATA_FILE, 'utf8').trim();
    if (!current) {
      fs.writeFileSync(DATA_FILE, JSON.stringify(getSeedData(), null, 2));
      return;
    }

    const parsed = JSON.parse(current);
    const seeded = getSeedData();
    const needsSeed = !parsed.suppliers || parsed.suppliers.length === 0 || !parsed.supplierCatalog || parsed.supplierCatalog.length === 0;
    if (needsSeed) {
      const merged = {
        ...seeded,
        ...parsed,
        areas: parsed.areas && parsed.areas.length ? parsed.areas : seeded.areas,
        users: parsed.users && parsed.users.length ? parsed.users : seeded.users,
        materials: parsed.materials && parsed.materials.length ? parsed.materials : seeded.materials,
        suppliers: parsed.suppliers && parsed.suppliers.length ? parsed.suppliers : seeded.suppliers,
        supplierCatalog: parsed.supplierCatalog && parsed.supplierCatalog.length ? parsed.supplierCatalog : seeded.supplierCatalog,
        rules: parsed.rules && parsed.rules.length ? parsed.rules : seeded.rules,
        withdrawals: parsed.withdrawals || [],
        alerts: parsed.alerts || [],
        purchaseOrders: parsed.purchaseOrders || [],
        events: parsed.events || []
      };
      fs.writeFileSync(DATA_FILE, JSON.stringify(merged, null, 2));
    }
  } catch (error) {
    fs.writeFileSync(DATA_FILE, JSON.stringify(getSeedData(), null, 2));
  }
}

function readStore() {
  ensureDataFile();
  return JSON.parse(fs.readFileSync(DATA_FILE, 'utf8'));
}

function writeStore(store) {
  fs.writeFileSync(DATA_FILE, JSON.stringify(store, null, 2));
}

function getUserById(store, userId) {
  return store.users.find((user) => user.id === userId);
}

function getAreaById(store, areaId) {
  return store.areas.find((area) => area.id === areaId);
}

function addEvent(store, type, message, entityId = null) {
  const event = {
    id: makeId('EVT'),
    type,
    message,
    entityId,
    at: new Date().toISOString()
  };
  store.events.push(event);
  return event;
}

function isUserAllowedForArea(store, userId, areaId) {
  const user = getUserById(store, userId);
  if (!user) return false;
  return user.role === 'acquisti' || user.areaIds.includes(areaId);
}

function evaluateMissingCodes(store, withdrawalId, missingCodes, userId) {
  const withdrawal = store.withdrawals.find((item) => item.id === withdrawalId);
  if (!withdrawal) return [];

  const actions = missingCodes.map((code) => {
    const materialExists = store.materials.some((mat) => mat.code === code);
    const alert = {
      id: makeId('ALERT'),
      withdrawalId,
      materialCode: code,
      status: 'pending',
      source: userId,
      reason: materialExists ? 'Materiale non disponibile in stock' : 'Codice materiale non presente in anagrafica',
      createdAt: new Date().toISOString()
    };
    store.alerts.push(alert);
    addEvent(store, 'missing_material', `Codice mancante rilevato: ${code}`, withdrawalId);

    return {
      code,
      materialExists,
      alertId: alert.id,
      buyerTarget: getAreaById(store, withdrawal.areaId)?.buyerOffice || 'Ufficio Acquisti',
      canAutoConfirm: isUserAllowedForArea(store, userId, withdrawal.areaId)
    };
  });

  return actions;
}

function safeParseDocumentText(text) {
  return String(text || '')
    .replace(/\r/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

function extractCodesFromText(rawText) {
  const text = safeParseDocumentText(rawText);
  const candidates = text.match(/[A-Z]{2,}[A-Z0-9\-_\.]{2,}|[A-Z0-9]{3,}-\d{2,}|\d{2,}[A-Z]{2,}[A-Z0-9\-_\.]{1,}/g) || [];
  return [...new Set(candidates.map((value) => value.trim()).filter(Boolean))];
}

function generateMechanicalBom(fileName, text, fallbackQty = 1) {
  const normalizedName = path.basename(fileName || 'meccanica');
  const baseName = normalizedName.split('.')[0].replace(/[_-]+/g, ' ');
  const fromText = extractCodesFromText(text || '');
  const baseCandidates = [
    baseName,
    ...fromText,
    ...((normalizedName.match(/[A-Z]{2,}[0-9]{2,}/g) || []).map((value) => value.trim())),
    ...((normalizedName.match(/[A-Z0-9]+\d+[A-Z0-9]*/g) || []).map((value) => value.trim()))
  ].filter(Boolean);

  const uniqueItems = [...new Set(baseCandidates.map((value) => value.replace(/\s+/g, '-').toUpperCase()))]
    .slice(0, 10)
    .map((value, index) => ({
      id: makeId('MECH'),
      partCode: value,
      description: `${baseName || 'Componente meccanico'} - ${index + 1}`,
      qty: fallbackQty,
      sourceFile: normalizedName,
      sourceType: 'mechanical-drawing'
    }));

  return uniqueItems.length ? uniqueItems : [{
    id: makeId('MECH'),
    partCode: `MECH-${Date.now()}`,
    description: `Componente generato da ${normalizedName}`,
    qty: fallbackQty,
    sourceFile: normalizedName,
    sourceType: 'mechanical-drawing'
  }];
}

async function externalSearchFallback(query) {
  const searchTerms = [query, `${query} Schneider Electric`, `${query} Pizzato`].filter(Boolean);
  const results = [];

  for (const term of searchTerms) {
    try {
      const url = `https://duckduckgo.com/html/?q=${encodeURIComponent(term)}`;
      const response = await fetch(url, { headers: { 'User-Agent': 'Mozilla/5.0' } });
      const text = await response.text();
      const matches = [...text.matchAll(/<a rel="nofollow" class="result-link" href="(.*?)".*?>(.*?)<\/a>/g)].slice(0, 5);

      for (const [, href, title] of matches) {
        const cleanTitle = title.replace(/<.*?>/g, '').trim();
        if (cleanTitle) {
          results.push({
            source: 'external-web',
            title: cleanTitle,
            url: href,
            snippet: `${term} - ricerca esterna`
          });
        }
      }
    } catch (error) {
      // ignora errori di rete e continua con altri provider
    }
  }

  return [...new Map(results.map((item) => [item.url || item.title, item])).values()].slice(0, 8);
}

app.use(cors());
app.use(express.json({ limit: '20mb' }));
app.use(express.static(path.join(__dirname, 'public')));

app.get('/api/dashboard', (_, res) => {
  const store = readStore();
  const summary = {
    withdrawals: store.withdrawals.length,
    openWithdrawals: store.withdrawals.filter((w) => w.status === 'open').length,
    missing: store.alerts.filter((a) => a.status === 'pending').length,
    purchaseOrders: store.purchaseOrders.length,
    users: store.users.length,
    materials: store.materials.length
  };

  res.json({
    summary,
    areas: store.areas,
    users: store.users,
    materials: store.materials,
    withdrawals: store.withdrawals,
    missingCodes: store.alerts,
    purchaseOrders: store.purchaseOrders,
    events: store.events.slice(-20).reverse()
  });
});

app.post('/api/areas', (req, res) => {
  const store = readStore();
  const area = {
    id: makeId('AREA'),
    name: req.body.name,
    code: req.body.code,
    buyerOffice: req.body.buyerOffice || 'Ufficio Acquisti'
  };
  store.areas.push(area);
  addEvent(store, 'area_created', `Area creata: ${area.name}`, area.id);
  writeStore(store);
  res.status(201).json(area);
});

app.post('/api/users', (req, res) => {
  const store = readStore();
  const user = {
    id: makeId('USER'),
    name: req.body.name,
    email: req.body.email,
    role: req.body.role || 'operatore',
    areaIds: req.body.areaIds || []
  };
  store.users.push(user);
  addEvent(store, 'user_created', `Utente creato: ${user.name}`, user.id);
  writeStore(store);
  res.status(201).json(user);
});

app.post('/api/materials', (req, res) => {
  const store = readStore();
  const material = {
    id: makeId('MAT'),
    code: req.body.code,
    description: req.body.description,
    stock: Number(req.body.stock || 0)
  };
  store.materials.push(material);
  addEvent(store, 'material_created', `Materiale creato: ${material.code}`, material.id);
  writeStore(store);
  res.status(201).json(material);
});

app.post('/api/withdrawals', (req, res) => {
  const store = readStore();
  const user = getUserById(store, req.body.userId);
  const area = getAreaById(store, req.body.areaId);

  if (!user || !area) {
    return res.status(400).json({ message: 'Utente o area non validi' });
  }

  const withdrawal = {
    id: makeId('DIST'),
    userId: user.id,
    areaId: area.id,
    status: 'open',
    createdAt: new Date().toISOString(),
    notes: req.body.notes || '',
    items: [],
    missingCodes: [],
    events: [{ id: makeId('EVT'), type: 'created', message: `Distinta lanciata da ${user.name}`, at: new Date().toISOString() }]
  };

  store.withdrawals.push(withdrawal);
  addEvent(store, 'withdrawal_created', `Distinta creata per ${area.name}`, withdrawal.id);
  writeStore(store);
  res.status(201).json(withdrawal);
});

app.post('/api/withdrawals/:id/items', (req, res) => {
  const store = readStore();
  const withdrawal = store.withdrawals.find((item) => item.id === req.params.id);
  if (!withdrawal) return res.status(404).json({ message: 'Distinta non trovata' });

  const newItem = {
    id: makeId('LINE'),
    materialCode: req.body.materialCode,
    qty: Number(req.body.qty || 0),
    status: 'ready'
  };

  withdrawal.items.push(newItem);
  addEvent(store, 'item_added', `Materiale aggiunto: ${newItem.materialCode}`, withdrawal.id);
  writeStore(store);
  res.status(201).json(newItem);
});

app.post('/api/withdrawals/:id/upload-pdf', upload.single('file'), async (req, res) => {
  const store = readStore();
  const withdrawal = store.withdrawals.find((item) => item.id === req.params.id);
  if (!withdrawal) return res.status(404).json({ message: 'Distinta non trovata' });
  if (!req.file) return res.status(400).json({ message: 'File PDF mancante' });

  try {
    const pdfData = await pdfParse(fs.readFileSync(req.file.path));
    const text = pdfData.text || '';
    const codes = [...new Set((text.match(/[A-Z0-9][A-Z0-9\-\.]{2,}/g) || []).map((code) => code.trim()))];
    const missingCodes = codes.filter((code) => !store.materials.some((mat) => mat.code === code));

    if (missingCodes.length === 0) {
      withdrawal.missingCodes = [];
      addEvent(store, 'pdf_processed', 'Nessun codice mancante rilevato dal PDF', withdrawal.id);
      writeStore(store);
      return res.json({ missingCodes: [], message: 'Nessun codice mancante rilevato.' });
    }

    withdrawal.missingCodes = missingCodes;
    const evaluations = evaluateMissingCodes(store, withdrawal.id, missingCodes, withdrawal.userId);
    addEvent(store, 'pdf_processed', `Rilevati ${missingCodes.length} codici mancanti`, withdrawal.id);
    writeStore(store);
    res.status(200).json({ missingCodes, evaluations, message: 'Codici mancanti rilevati e segnalati all’ufficio acquisti.' });
  } catch (err) {
    console.error(err);
    res.status(500).json({ message: 'Errore nel parsing del PDF', details: err.message });
  } finally {
    if (req.file && fs.existsSync(req.file.path)) {
      fs.unlinkSync(req.file.path);
    }
  }
});

app.post('/api/import-spreadsheet', upload.single('file'), async (req, res) => {
  try {
    if (!req.file) return res.status(400).json({ message: 'File mancante' });

    const workbook = XLSX.readFile(req.file.path);
    const sheetName = workbook.SheetNames[0];
    const rows = XLSX.utils.sheet_to_json(workbook.Sheets[sheetName], { defval: '' });

    res.json({
      file: req.file.originalname,
      sheet: sheetName,
      rows,
      message: 'Foglio importato correttamente.'
    });
  } catch (error) {
    res.status(500).json({ message: 'Errore nell’importazione del file Excel/CSV', details: error.message });
  } finally {
    if (req.file && fs.existsSync(req.file.path)) {
      fs.unlinkSync(req.file.path);
    }
  }
});

app.post('/api/import-document', upload.single('file'), async (req, res) => {
  try {
    if (!req.file) return res.status(400).json({ message: 'File mancante' });
    const ext = path.extname(req.file.originalname).toLowerCase();
    let text = '';

    if (ext === '.docx') {
      const result = await mammoth.extractRawText({ path: req.file.path });
      text = result.value || '';
    } else if (ext === '.pdf') {
      const parsed = await pdfParse(fs.readFileSync(req.file.path));
      text = parsed.text || '';
    } else {
      text = fs.readFileSync(req.file.path, 'utf8');
    }

    res.json({
      file: req.file.originalname,
      text: safeParseDocumentText(text),
      extractedCodes: extractCodesFromText(text),
      message: 'Documento importato correttamente.'
    });
  } catch (error) {
    res.status(500).json({ message: 'Errore nell’importazione del documento', details: error.message });
  } finally {
    if (req.file && fs.existsSync(req.file.path)) {
      fs.unlinkSync(req.file.path);
    }
  }
});

app.post('/api/upload-mechanical-file', upload.single('file'), async (req, res) => {
  try {
    if (!req.file) return res.status(400).json({ message: 'File mancante' });

    const ext = path.extname(req.file.originalname).toLowerCase().replace('.', '');
    const isMechanical = MECHANICAL_EXTENSIONS.has(ext);

    let text = '';
    if (['pdf', 'txt', 'docx'].includes(ext)) {
      if (ext === 'pdf') {
        const parsed = await pdfParse(fs.readFileSync(req.file.path));
        text = parsed.text || '';
      } else if (ext === 'docx') {
        const parsed = await mammoth.extractRawText({ path: req.file.path });
        text = parsed.value || '';
      } else {
        text = fs.readFileSync(req.file.path, 'utf8');
      }
    }

    const bom = generateMechanicalBom(req.file.originalname, text, 1);

    res.json({
      isMechanical,
      sourceFile: req.file.originalname,
      extension: ext,
      bom,
      message: isMechanical
        ? 'Disegno meccanico rilevato: distinta preliminare generata.'
        : 'File caricato: il sistema ha creato una distinta preliminare di supporto.'
    });
  } catch (error) {
    res.status(500).json({ message: 'Errore nella generazione della distinta meccanica', details: error.message });
  } finally {
    if (req.file && fs.existsSync(req.file.path)) {
      fs.unlinkSync(req.file.path);
    }
  }
});

app.get('/api/catalog-search', async (req, res) => {
  const store = readStore();
  const query = (req.query.q || '').toString().trim();

  if (!query) {
    return res.json({ results: [], source: 'internal', message: 'Nessun termine di ricerca specificato.' });
  }

  const normalized = query.toLowerCase();
  const localMatches = [
    ...store.materials,
    ...store.supplierCatalog,
    ...store.suppliers
  ].filter((entry) => {
    const searchable = [
      entry.name,
      entry.description,
      entry.code,
      entry.family,
      entry.supplier,
      entry.email,
      entry.website,
      entry.materialCode,
      entry.model
    ].filter(Boolean).join(' ').toLowerCase();
    return searchable.includes(normalized);
  });

  if (localMatches.length > 0) {
    return res.json({
      results: localMatches.slice(0, 10),
      source: 'internal-catalog',
      message: 'Risultati trovati nel catalogo locale o in fornitori importati.'
    });
  }

  const externalResults = await externalSearchFallback(query);
  return res.json({
    results: externalResults,
    source: 'external-search',
    message: 'Nessun risultato locale: avviata ricerca esterna su cataloghi e web.'
  });
});

app.post('/api/import-catalog', upload.single('file'), async (req, res) => {
  try {
    if (!req.file) return res.status(400).json({ message: 'File mancante' });
    const supplier = (req.body.supplier || 'Fornitore generico').toString();
    const ext = path.extname(req.file.originalname).toLowerCase();

    let rows = [];

    if (['.csv', '.xlsx', '.xls'].includes(ext)) {
      const workbook = XLSX.readFile(req.file.path);
      const sheet = workbook.Sheets[workbook.SheetNames[0]];
      rows = XLSX.utils.sheet_to_json(sheet, { defval: '' });
    } else {
      return res.status(400).json({ message: 'Formato file non supportato per il catalogo. Usa CSV o Excel.' });
    }

    const store = readStore();
    const catalogEntries = rows
      .map((row) => {
        const code = row.code || row.Codice || row.codice || row.part || row.PartNumber || row['Part Number'];
        const name = row.name || row.Nome || row.descrizione || row.Description || row['Descrizione'];
        if (!code || !name) return null;
        return {
          id: makeId('CAT'),
          supplier,
          code: String(code).trim(),
          name: String(name).trim(),
          description: String(row.description || row.descrizione || row.Descrizione || name).trim(),
          family: String(row.family || row.Famiglia || row.category || row.Categoria || 'Generico').trim(),
          uom: String(row.uom || row.unita || row.unit || 'pz').trim(),
          price: Number(row.price || row.Prezzo || row['Prezzo unitario'] || 0),
          source: 'imported-catalog'
        };
      })
      .filter(Boolean);

    store.supplierCatalog.push(...catalogEntries);
    if (!store.suppliers.some((item) => item.name === supplier)) {
      store.suppliers.push({ id: makeId('SUP'), name: supplier, code: supplier.slice(0, 4).toUpperCase(), website: '' });
    }
    addEvent(store, 'catalog_imported', `Catalogo importato: ${supplier} (${catalogEntries.length} componenti)`, null);
    writeStore(store);

    res.json({
      supplier,
      imported: catalogEntries.length,
      message: `Catalogo importato correttamente per ${supplier}.`
    });
  } catch (error) {
    res.status(500).json({ message: 'Errore nell’importazione del catalogo fornitore', details: error.message });
  } finally {
    if (req.file && fs.existsSync(req.file.path)) {
      fs.unlinkSync(req.file.path);
    }
  }
});

app.get('/api/purchase-orders', (_, res) => {
  const store = readStore();
  res.json(store.purchaseOrders);
});

app.post('/api/purchase-orders', (req, res) => {
  const store = readStore();
  const { materialCode, withdrawalId, supplier, requestedByUserId } = req.body;

  const purchaseOrder = {
    id: makeId('PO'),
    withdrawalId,
    materialCode,
    supplier: supplier || 'Fornitore standard',
    qty: Number(req.body.qty || 1),
    status: 'confirmed',
    requestedByUserId: requestedByUserId || null,
    createdAt: new Date().toISOString()
  };

  store.purchaseOrders.push(purchaseOrder);
  const alert = store.alerts.find((item) => item.materialCode === materialCode && item.status === 'pending');
  if (alert) {
    alert.status = 'confirmed';
  }

  addEvent(store, 'purchase_order_created', `Conferma ordine generata per ${materialCode}`, withdrawalId || purchaseOrder.id);
  writeStore(store);
  res.status(201).json(purchaseOrder);
});

app.get('*', (_, res) => {
  res.sendFile(path.join(__dirname, 'public', 'index.html'));
});

app.listen(PORT, () => {
  console.log(`Server in esecuzione su http://localhost:${PORT}`);
});
