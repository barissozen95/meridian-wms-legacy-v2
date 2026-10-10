namespace MeridianWms.Web.Services;

// Replaces Web.config <appSettings> LowStockThreshold / DefaultWarehouseCode.
public class WmsOptions
{
    public const string Section = "Wms";

    public string DefaultWarehouseCode { get; set; } = "WH-CENTRAL";
    public int LowStockThreshold { get; set; } = 10;
}
