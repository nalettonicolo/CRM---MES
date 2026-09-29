namespace CrmMes.Core.Models;

/// <summary>Ore lavorate su una commessa registrate a mano: il lavoro che non passa dall'avvio/fine di
/// una fase al terminale (cablaggio a banco fuori ciclo, installazione dal cliente, collaudo in
/// cantiere, rilavorazioni). Valorizzate con la tariffa del centro di lavoro indicato, o in mancanza
/// di quello della fase collegata.</summary>
public class LaborEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;
    public Guid? WorkOrderOperationId { get; set; }
    public WorkOrderOperation? Operation { get; set; }
    public Guid? WorkCenterId { get; set; }
    public WorkCenter? WorkCenter { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Nome dell'operatore come istantanea leggibile, anche se l'utente viene poi rinominato.</summary>
    public string? OperatorName { get; set; }
    public decimal Minutes { get; set; }
    public DateTime WorkDate { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
