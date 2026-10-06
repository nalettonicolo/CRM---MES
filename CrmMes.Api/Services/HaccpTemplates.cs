namespace CrmMes.Api.Services;

/// <summary>Starting points for the HACCP plan, by type of process. They only propose the usual critical
/// control points of each process: the company checks them, adapts the limits to its own plan and
/// applies them. Numeric points carry limits; yes/no points carry none.</summary>
public static class HaccpTemplates
{
    public sealed record Point(
        string Name,
        string Location,
        string Hazard,
        string? Unit,
        decimal? MinValue,
        decimal? MaxValue,
        string Frequency,
        string CorrectiveActionHint);

    public sealed record Template(string Key, string Name, string Description, IReadOnlyList<Point> Points);

    public static readonly IReadOnlyList<Template> All =
    [
        new("ricevimento", "Ricevimento merci deperibili", "Catena del freddo alla consegna delle materie prime.",
        [
            new("Temperatura al ricevimento", "Ricevimento", "Materia prima non conforme alla catena del freddo", "°C", null, 4m,
                "Ad ogni consegna", "Rifiutare o segregare il lotto, annotarlo sul DDT e avvisare il fornitore"),
        ]),
        new("refrigerazione", "Refrigerazione e conservazione", "Celle e frigoriferi di conservazione.",
        [
            new("Temperatura cella di conservazione", "Cella frigo", "Crescita di microrganismi", "°C", 0m, 4m,
                "Due volte al giorno", "Verificare il guasto, spostare il prodotto in una cella conforme e valutare il lotto"),
        ]),
        new("abbattimento", "Abbattimento rapido", "Raffreddamento rapido dopo la lavorazione.",
        [
            new("Temperatura al cuore dopo abbattimento", "Abbattitore", "Crescita di microrganismi durante il raffreddamento", "°C", null, 3m,
                "Ad ogni ciclo", "Ripetere il ciclo di abbattimento e non passare il prodotto allo stoccaggio finché non è conforme"),
        ]),
        new("cottura", "Cottura e termo-trattamento", "Forni, marmitte e trattamenti termici.",
        [
            new("Temperatura al cuore a fine cottura", "Cottura", "Sopravvivenza di patogeni (Salmonella, Listeria)", "°C", 72m, null,
                "Ogni lotto", "Prolungare la cottura e rimisurare; il lotto resta bloccato fino a esito conforme"),
        ]),
        new("corpi-estranei", "Controllo corpi estranei", "Rilevatori di metalli e controlli visivi di linea.",
        [
            new("Prova del metal detector con campioni test", "Linea di confezionamento", "Corpi metallici nel prodotto", null, null, null,
                "Inizio turno e ogni due ore", "Fermare la linea, isolare il prodotto dall'ultima prova conforme e verificare il rilevatore"),
        ]),
        new("sanificazione", "Pulizia e sanificazione", "Pulizia delle superfici a contatto con il prodotto.",
        [
            new("Pulizia e sanificazione delle superfici a contatto", "Reparto produzione", "Contaminazione microbiologica", null, null, null,
                "Fine turno", "Ripetere la sanificazione prima della produzione e annotarne la causa"),
        ]),
    ];

    public static Template? Find(string key) =>
        All.FirstOrDefault(template => string.Equals(template.Key, key, StringComparison.OrdinalIgnoreCase));
}
