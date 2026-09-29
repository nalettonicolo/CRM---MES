using CrmMes.Core.Models;

namespace CrmMes.Api.Services;

/// <summary>Builds a new Draft work order the same way wherever one is created — by hand from the
/// work orders screen, or in bulk when an accepted quote is converted — so both paths snapshot the
/// routing and generate per-unit tracking identically.</summary>
public sealed class WorkOrderFactory
{
    /// <param name="product">Must have <see cref="Product.RoutingSteps"/> loaded.</param>
    public WorkOrder Build(
        Product product,
        decimal quantity,
        string? code = null,
        string? productLotNumber = null,
        Guid? areaId = null,
        string? customerReference = null,
        Guid? customerId = null,
        Guid? quoteId = null,
        DateTime? dueDate = null,
        string? notes = null)
    {
        var order = new WorkOrder
        {
            // Random suffix avoids collisions when two work orders are created within the same second.
            Code = string.IsNullOrWhiteSpace(code)
                ? $"WO-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}"
                : code.Trim(),
            ProductLotNumber = string.IsNullOrWhiteSpace(productLotNumber)
                ? $"LOT-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}"
                : productLotNumber.Trim(),
            ProductId = product.Id,
            Quantity = quantity,
            AreaId = areaId,
            CustomerReference = string.IsNullOrWhiteSpace(customerReference) ? null : customerReference.Trim(),
            CustomerId = customerId,
            QuoteId = quoteId,
            DueDate = dueDate,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Status = "Draft"
        };

        // Snapshot the product's routing now: later edits to the product's template must not
        // retroactively change a job that may already be on the floor.
        foreach (var step in product.RoutingSteps.OrderBy(s => s.SequenceNumber))
        {
            order.Operations.Add(new WorkOrderOperation
            {
                WorkOrderId = order.Id,
                SequenceNumber = step.SequenceNumber,
                Name = step.Name,
                Description = step.Description,
                WorkCenter = step.WorkCenter,
                EstimatedMinutes = step.EstimatedMinutes,
                Status = "Pending"
            });
        }

        // Per-serial tracking only makes sense for a whole number of discrete units — a continuous or
        // bulk quantity (e.g. 2.5 kg) has nothing to number, so no units are generated for it and it
        // keeps using the batch-level lot number and quality approximation instead.
        if (order.Quantity == Math.Floor(order.Quantity) && order.Quantity > 0)
        {
            for (var sequence = 1; sequence <= (int)order.Quantity; sequence++)
            {
                var unit = new WorkOrderUnit
                {
                    WorkOrderId = order.Id,
                    SequenceNumber = sequence,
                    SerialNumber = $"{order.Code}-{sequence:000}",
                    Status = "Pending"
                };

                // Per-unit phase history starts as a full Pending grid — one row per operation — so
                // querying "what has unit N gone through" always has an answer, even before the first
                // phase starts. StartOperation/CompleteOperation project the batch-level timing onto
                // these rows for every unit that's still Pending.
                foreach (var operation in order.Operations)
                {
                    unit.Operations.Add(new WorkOrderUnitOperation
                    {
                        WorkOrderUnitId = unit.Id,
                        WorkOrderOperationId = operation.Id,
                        Status = "Pending"
                    });
                }

                order.Units.Add(unit);
            }
        }

        return order;
    }
}
