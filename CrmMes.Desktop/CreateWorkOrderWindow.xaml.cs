using System.Windows;

namespace CrmMes.Desktop;

public partial class CreateWorkOrderWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid? _editingWorkOrderId;

    public bool Created { get; private set; }

    /// <summary>Creation mode.</summary>
    public CreateWorkOrderWindow(ApiClient apiClient, IReadOnlyList<ProductSummaryDto> products, IReadOnlyList<AreaDto> areas)
        : this(apiClient, products, areas, existing: null)
    {
    }

    /// <summary>Edit mode (solo commesse in bozza): pre-fills i campi modificabili e disabilita la
    /// scelta del prodotto (l'API non permette di cambiare prodotto dopo la creazione, perché le fasi
    /// sono già state fotografate dal ciclo di lavoro del prodotto originale).</summary>
    public CreateWorkOrderWindow(ApiClient apiClient, IReadOnlyList<ProductSummaryDto> products, IReadOnlyList<AreaDto> areas, WorkOrderDetailDto? existing)
    {
        InitializeComponent();
        _apiClient = apiClient;
        ProductCombo.ItemsSource = products;
        AreaCombo.ItemsSource = areas;

        if (existing is not null)
        {
            _editingWorkOrderId = existing.Id;
            Title = $"Modifica commessa {existing.Code}";
            TitleText.Text = $"Modifica commessa {existing.Code}";
            CreateButton.Content = "Salva modifiche";
            ProductCombo.SelectedItem = products.FirstOrDefault(product => product.Id == existing.ProductId);
            ProductCombo.IsEnabled = false;
            QuantityBox.Text = existing.Quantity.ToString();
            AreaCombo.SelectedItem = areas.FirstOrDefault(area => area.Id == existing.AreaId);
            CustomerReferenceBox.Text = existing.CustomerReference;
            DueDatePicker.SelectedDate = existing.DueDate;
            NotesBox.Text = existing.Notes;
        }
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(QuantityBox.Text, out var quantity) || quantity <= 0)
        {
            ErrorText.Text = "Inserisci una quantità valida (maggiore di zero).";
            return;
        }

        var areaId = (AreaCombo.SelectedItem as AreaDto)?.Id;
        var customerReference = string.IsNullOrWhiteSpace(CustomerReferenceBox.Text) ? null : CustomerReferenceBox.Text.Trim();
        var notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();

        CreateButton.IsEnabled = false;
        try
        {
            if (_editingWorkOrderId is Guid workOrderId)
            {
                await _apiClient.EditWorkOrderAsync(workOrderId, quantity, areaId, customerReference, DueDatePicker.SelectedDate, notes);
            }
            else
            {
                if (ProductCombo.SelectedItem is not ProductSummaryDto product)
                {
                    ErrorText.Text = "Seleziona un prodotto.";
                    CreateButton.IsEnabled = true;
                    return;
                }

                await _apiClient.CreateWorkOrderAsync(product.Id, quantity, areaId, customerReference, DueDatePicker.SelectedDate, notes);
            }

            Created = true;
            Close();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            CreateButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
