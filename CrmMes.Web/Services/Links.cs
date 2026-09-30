namespace CrmMes.Web.Services;

public static class Links
{
    public static string DetailHref(this WorkOrderLookup order) => $"commesse/{order.Id}";

    public static string DetailHref(this WorkOrderSummary order) => $"commesse/{order.Id}";

    public static string DetailHref(this Customer customer) => $"clienti/{customer.Id}";
}
