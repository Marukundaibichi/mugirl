namespace Mugirl.Features.Corporate;

/// <summary>
/// 交易与订购目录的界面文案（<c>Mugirl.CorporateTradeBrowser.*</c>）。键写成完整常量，便于检索与离线翻译检查，
/// 也省去 1.6 每次调用时的键名拼接。参数按 1.6 的写法先转成字符串（整数用当前区域性）。
/// </summary>
internal static class CorporateBrowserText
{
    internal const string All = "Mugirl.CorporateTradeBrowser.All";
    internal const string Materials = "Mugirl.CorporateTradeBrowser.Materials";
    internal const string Food = "Mugirl.CorporateTradeBrowser.Food";
    internal const string Medicine = "Mugirl.CorporateTradeBrowser.Medicine";
    internal const string Weapons = "Mugirl.CorporateTradeBrowser.Weapons";
    internal const string Apparel = "Mugirl.CorporateTradeBrowser.Apparel";
    internal const string Rare = "Mugirl.CorporateTradeBrowser.Rare";
    internal const string Other = "Mugirl.CorporateTradeBrowser.Other";
    internal const string SortName = "Mugirl.CorporateTradeBrowser.SortName";
    internal const string SortPriceLow = "Mugirl.CorporateTradeBrowser.SortPriceLow";
    internal const string SortPriceHigh = "Mugirl.CorporateTradeBrowser.SortPriceHigh";
    internal const string GridView = "Mugirl.CorporateTradeBrowser.GridView";
    internal const string ListView = "Mugirl.CorporateTradeBrowser.ListView";
    internal const string Results = "Mugirl.CorporateTradeBrowser.Results";
    internal const string PurchaseDesk = "Mugirl.CorporateTradeBrowser.PurchaseDesk";
    internal const string ProcurementDesk = "Mugirl.CorporateTradeBrowser.ProcurementDesk";
    internal const string UpdatingCatalog = "Mugirl.CorporateTradeBrowser.UpdatingCatalog";
    internal const string RefreshIn = "Mugirl.CorporateTradeBrowser.RefreshIn";
    internal const string Available = "Mugirl.CorporateTradeBrowser.Available";
    internal const string SoldOut = "Mugirl.CorporateTradeBrowser.SoldOut";
    internal const string UnitPrice = "Mugirl.CorporateTradeBrowser.UnitPrice";
    internal const string PurchasePrice = "Mugirl.CorporateTradeBrowser.PurchasePrice";
    internal const string Stock = "Mugirl.CorporateTradeBrowser.Stock";
    internal const string Quantity = "Mugirl.CorporateTradeBrowser.Quantity";
    internal const string Total = "Mugirl.CorporateTradeBrowser.Total";
    internal const string InsufficientFunds = "Mugirl.CorporateTradeBrowser.InsufficientFunds";
    internal const string Mass = "Mugirl.CorporateTradeBrowser.Mass";
    internal const string DeliveryTo = "Mugirl.CorporateTradeBrowser.DeliveryTo";
    internal const string Lead = "Mugirl.CorporateTradeBrowser.Lead";
    internal const string SpecialLead = "Mugirl.CorporateTradeBrowser.SpecialLead";
    internal const string Days = "Mugirl.CorporateTradeBrowser.Days";
    internal const string Preparation = "Mugirl.CorporateTradeBrowser.Preparation";
    internal const string Material = "Mugirl.CorporateTradeBrowser.Material";
    internal const string Quality = "Mugirl.CorporateTradeBrowser.Quality";
    internal const string Cancellation = "Mugirl.CorporateTradeBrowser.Cancellation";
    internal const string NoResults = "Mugirl.CorporateTradeBrowser.NoResults";
    internal const string ResetFilters = "Mugirl.CorporateTradeBrowser.ResetFilters";
    internal const string SelectProduct = "Mugirl.CorporateTradeBrowser.SelectProduct";
    internal const string ProductDetails = "Mugirl.CorporateTradeBrowser.ProductDetails";
    internal const string Unavailable = "Mugirl.CorporateTradeBrowser.Unavailable";
    internal const string QuotePending = "Mugirl.CorporateTradeBrowser.QuotePending";
    internal const string RemainingDays = "Mugirl.CorporateTradeBrowser.RemainingDays";
    internal const string Paid = "Mugirl.CorporateTradeBrowser.Paid";
    internal const string ConfirmCancellation = "Mugirl.CorporateTradeBrowser.ConfirmCancellation";

    // StaticCacheLifecycle: 不可变的翻译键表，下标即目录分类与排序方式的序号（1.6 的 CatalogCategoryKeys/CatalogSortKeys）。
    internal static readonly string[] CategoryKeys = [All, Materials, Food, Medicine, Weapons, Apparel, Rare, Other];
    internal static readonly string[] SortKeys = [SortName, SortPriceLow, SortPriceHigh];

    internal static string Get(string key) => key.Translate();

    internal static string Get(string key, string arg) => key.Translate(arg);

    internal static string Get(string key, int arg) => key.Translate(arg.ToString());

    internal static string Get(string key, string arg1, string arg2) => key.Translate(arg1, arg2);

    internal static string Get(string key, int arg1, int arg2) => key.Translate(arg1.ToString(), arg2.ToString());

    internal static string Get(string key, string arg1, int arg2) => key.Translate(arg1, arg2.ToString());
}
