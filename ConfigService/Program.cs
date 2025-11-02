
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using static System.Formats.Asn1.AsnWriter;

namespace ConfigService;

// Order interface with relevant properties.
// Properties can be null or empty string.
public interface IOrder
{
    public string Strategy { get; }
    public string Aggression { get; }
    public string Country { get; }
    public string AssetType { get; }
    public string Account { get; }
    public string TraderId { get; }
}

// Order class implementing IOrder interface.
// Properties can be null or empty string.
public class Order : IOrder
{
    public string Strategy { get; set; }
    public string Aggression { get; set; }
    public string Country { get; set; }
    public string AssetType { get; set; }
    public string Account { get; set; }
    public string TraderId { get; set; }
}


// ConfigService class to load manifest and config files,
// determine applicable manifests for an order,
// and retrieve config values based on the most specific manifest.
// Assumes well-formed CSV files with consistent columns.
public class ConfigService
{
    //manifest& config  tables. Key -> 'manifest', Dictionary of columns and values.  
    private Dictionary<string, Dictionary<string, Object>> _manifestTable = new Dictionary<string, Dictionary<string, Object>>();
	private Dictionary<string, Dictionary<string, Object>> _configsTable = new Dictionary<string, Dictionary<string, Object>>();
    // store headers from manifest.csv in original order
    private List<string> _manifestHeaders = new List<string>();
    // store headers from cfgs.csv in original order
    private List<string> _configsHeaders = new List<string>();
    // Weights per header for scoring specificity.
    Dictionary<string, int> _headerWeights = new Dictionary<string, int>() ;

    // Merged manifest and config table for lookup.
    private Dictionary<string, Dictionary<string, Object>> _manifestAndconfigsTable = new Dictionary<string, Dictionary<string, Object>>();


    // Constructor: load manifest and config files.
    // Initialize header weights.
    
    public ConfigService(string maifestFile,string configFile)
    {
        // Load manifest and config files.  
        LoadManifestAndConfig(maifestFile, configFile);
        // Initialize header weights based on manifest headers.
        InitializeHeaderWeights();
    }
    // Get applicable manifests for the given order parameters.
    // Returns dictionary of manifest key to specificity score.
    // An empty dictionary indicates no applicable manifests.
    public Dictionary<string,int> GetApplicableManifests(in IOrder inputOrder)
    {
        // Scan _manifestAndconfigsTable using the header order in _manifestHeaders.
        // Assume all headers/values exist and match by equality or '*' wildcard.
        var results = new Dictionary<string, int>();

        // Prepare order values dictionary for easy lookup.
        var orderValues = new Dictionary<string, string>() {
            { "Strategy", inputOrder.Strategy?.ToUpper() ?? string.Empty},
            { "Aggression", inputOrder.Aggression?.ToUpper() ?? string.Empty },
            { "Country", inputOrder.Country?.ToUpper() ?? string.Empty },
            { "AssetType", inputOrder.AssetType?.ToUpper() ?? string.Empty },
            { "Account", inputOrder.Account?.ToUpper() ?? string.Empty },
            { "TraderId", inputOrder.TraderId?.ToUpper() ?? string.Empty}
        };

        foreach (var kv in _manifestAndconfigsTable)
        {
            var row = kv.Value;
            bool match = true;
            int score = 0;
            // iterate headers from _manifestHeaders and skip Manifest column
            foreach (var header in _manifestHeaders)
            {
                if (header.Equals("Manifest", StringComparison.OrdinalIgnoreCase))
                    continue;

                // assume value exists for header in row
                var mv = row[header];
                var ov = orderValues[header]; // assume header maps to a key in orderValues

                if (!(mv.ToString() == "*" || mv.ToString() == ov))
                {
                    match = false;
                    break;
                }
                // assume value exists; treat non-"*" as specific
                if (mv.ToString() != "*")
                {
                    if (_headerWeights.TryGetValue(header, out var weight))
                        score += (int)weight;
                    // if header not recognized, ignore (no weight)
                }
            }

            if (match)
                results[kv.Key] = score;
        }
        return results;
    }

    // Initialize header weights based on manifest headers order.
    // First header gets highest weight, last gets lowest.
    private void InitializeHeaderWeights()
    {
        _headerWeights.Clear();

        if (_manifestHeaders.Count == 0)
            return;
        int count = _manifestHeaders.Count;
        for (int i = 0; i < count; i++)
        {
            // weight = (count - i) * 10  -> first header => count*10, last => 10
            int weight = (count - i) * 10;
            _headerWeights[_manifestHeaders[i]] = weight;
        }
    }

    // Load manifest and config files into tables and merge them.
    // Merged table key -> 'Manifest', value -> dictionary of column name to value.
    private void LoadManifestAndConfig(string manifestFile, string configsFile)
    {

        _manifestTable.Clear();
        _manifestHeaders.Clear();

        try
        {
            var lines = File.ReadAllLines(manifestFile);
            LoadTable(lines, out _manifestTable, out _manifestHeaders);

            _configsTable.Clear();
            _configsHeaders.Clear();

            lines = File.ReadAllLines(configsFile);
            LoadTable(lines, out _configsTable, out _configsHeaders);
        }
        catch(Exception ex)
        {
            Trace.WriteLine($"Exception loading files: {ex.Message}");
            return;
        }

        // Merge (join) both table for the Look up.  
        foreach (var kv in _manifestTable)
        {
            var key = kv.Key;
            var manifestRow = kv.Value;
            var merged = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // copy manifest values
            foreach (var m in manifestRow)
                merged[m.Key] = m.Value;

            // if there is a matching config row, copy/override with config values
            if (_configsTable.TryGetValue(key, out var cfgRow))
            {
                foreach (var c in cfgRow)
                    merged[c.Key] = c.Value;
            }

            _manifestAndconfigsTable[key] = merged;
        }

        // Then add any config-only rows that didn't have a manifest counterpart
        foreach (var kv in _configsTable)
        {
            if (_manifestAndconfigsTable.ContainsKey(kv.Key))
                continue;

            var merged = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // copy config values
            foreach (var c in kv.Value)
                merged[c.Key] = c.Value;

            _manifestAndconfigsTable[kv.Key] = merged;
        }

        PrintTable(_manifestAndconfigsTable, _manifestHeaders.Union(_configsHeaders).ToList());

    }
        // Helper to load CSV lines into table: key -> 'Manifest', value -> dictionary of column name to value.
    // Assumes first line is header row.
    // Treat empty values as wildcard "*".
    // Assumes well-formed CSV with consistent number of columns.
    private void LoadTable(string[] lines, out Dictionary<string, Dictionary<string, Object>> table, out List<string> headers)
    {
        table = new Dictionary<string, Dictionary<string, Object>>();
        headers = new List<string>();
        if (lines == null || lines.Length == 0)
            return;
        headers = lines[0].Split(',').ToList(); // first line is header row.
        for (int r = 1; r < lines.Length; r++)
        {
            string line = lines[r];
            string[] fields = line.Split(',');
            string key = "";
            var row = new Dictionary<string, object>();
            for (int col = 0; col < headers.Count; col++)
            {
                string header = headers[col];
                string value = fields[col];
                if (String.IsNullOrEmpty(value))
                {
                    value = "*"; // treat empty as wildcard
                }
                row[header] = value.ToUpper();
                if (header == "Manifest")
                {
                    key = value;
                }
            }
            table[key] = row;
        }
    }
    private void PrintTable(Dictionary<string, Dictionary<string, Object>> table, List<string> headers)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers));
        foreach (var kv in table)
        {
            var row = kv.Value;
            List<string> fields = new List<string>();
            foreach (var header in headers)
            {
                if (row.TryGetValue(header, out var value) && value != null)
                {
                    fields.Add(value.ToString() ?? "");
                }
                else
                {
                    fields.Add("");
                }
            }
            sb.AppendLine(string.Join(",", fields));
        }
        Trace.WriteLine(sb.ToString());
    }

    // Get config value for the given paramSection, paramKey, and data type from the applicable manifests.
    // Returns empty string if not found.
    // Selects the manifest with the highest specificity score.

    private string GetConfigValue(Dictionary<string, int> manifests, string paramSection, string paramKey, string type)
    {
        string result = "";

        if (manifests == null || manifests.Count == 0)
            return result;

        // Iterate manifests in order of descending specificity score.
        // The first matching entry is the most specific.
        // So we return its value.
        try
        {
            foreach (var kv in manifests.OrderByDescending(kv => kv.Value))
            {
                string selectedManifest = kv.Key;
                var row = _manifestAndconfigsTable[selectedManifest];
                // find matching config entry
                if (row.TryGetValue("ParamSection", out var psObj) && row.TryGetValue("ParamKey", out var pkObj)
                    && psObj?.ToString() == paramSection.ToUpper() && pkObj?.ToString() == paramKey.ToUpper())
                {
                    if (row.TryGetValue("DataType", out var dtObj) && dtObj?.ToString() == type.ToUpper()
                       && row.TryGetValue("Value", out var valueObj) && valueObj != null)
                    {
                        return valueObj.ToString() ?? "";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Exception in GetConfigValue: {ex.Message}");
        }

        return result;
    }

    // public APIs 

    // Get int, decimal, string config values for the given order and paramSection/paramKey.
    // Uses the most specific applicable manifest for the order.
    // Returns defaultValue if not found or conversion fails.
    public int getIntConfig(IOrder order, string paramSection, string paramKey, int defaultValue)
    {
        Dictionary<string, int> manifests = GetApplicableManifests(order);
        //string selectedManifest = SelectManifest(manifests);
        if (manifests.Count <= 0) return defaultValue;

        string result = GetConfigValue(manifests, paramSection, paramKey, "int");

        if (!String.IsNullOrEmpty(result))
        {
            if (int.TryParse(result, out int intValue))
            {
                return intValue;
            }
        }
        return defaultValue;
    }
    public decimal getDecimalConfig(IOrder order, string paramSection, string paramKey, decimal defaultValue)
    {
        Dictionary<string, int> manifests = GetApplicableManifests(order);
        //string selectedManifest = SelectManifest(manifests);
        if (manifests.Count <= 0) return defaultValue;

        string result = GetConfigValue(manifests, paramSection, paramKey, "decimal");
        if (!String.IsNullOrEmpty(result))
        {
            if (decimal.TryParse(result, out decimal decValue))
            {
                return decValue;
            }
        }
        return defaultValue;
    }
    public string getStringConfig(IOrder order, string paramSection, string paramKey, string defaultValue)
    {
        Dictionary<string, int> manifests = GetApplicableManifests(order);
        //string selectedManifest = SelectManifest(manifests);
        if (manifests.Count <= 0) return defaultValue;

        string result =  GetConfigValue(manifests, paramSection, paramKey, "string");
         if (!String.IsNullOrEmpty(result))
        {
            return result;
        }
        return defaultValue;
    }


}


public class Program
{
    public static void Main(string[] args)
    {

        Trace.Listeners.Add(new ConsoleTraceListener());
        ConfigService configService = new ConfigService("InputFiles\\manifest.csv", "InputFiles\\cfgs.csv");

        //1.getIntConfig(IOrder order, "CloseAuction", "SendTimeOffsetSeconds", 23400)
            //For a VWAP order with aggression as M it'll return 600 (from VWAP)
            //For a VWAP order with aggression as P it'll return 900 (from VWAP_PASSIVE)
            //For a VWAP order with aggression as A it'll return 600 (from VWAP)
            //For a VWAP order with no aggression(null), it'll return 600(from VWAP)
            //For a VWAP order for Account = CLIENTXYZ, it'll return 2000(from VWAP_PASSIVE_XYZ)
            //For a TWAP order, it'll return 60 (only BASE manifest qualifies for this TWAP order)

        Order order = new Order()
        {
            Strategy = "",
            Aggression = "",
            Country = "",
            AssetType = "", 
            Account = "",
            TraderId = "Joe"
        };

        int intCfg;
        order.Strategy = "VWAP";
        order.Aggression = "M";
        intCfg = configService.getIntConfig(order, "CloseAuction", "SendTimeOffsetSeconds", 23400);
        Trace.WriteLine($"Int Config: {intCfg}");
        order.Aggression = "P";
        intCfg = configService.getIntConfig(order, "CloseAuction", "SendTimeOffsetSeconds", 23400);
        Trace.WriteLine($"Int Config: {intCfg}");
        order.Aggression = "A";
        intCfg = configService.getIntConfig(order, "CloseAuction", "SendTimeOffsetSeconds", 23400);
        Trace.WriteLine($"Int Config: {intCfg}");
        order.Aggression = null;
        intCfg = configService.getIntConfig(order, "CloseAuction", "SendTimeOffsetSeconds", 23400);
        Trace.WriteLine($"Int Config: {intCfg}");
        order.Aggression = "P";
        order.Account = "CLIENTXYZ";
        intCfg = configService.getIntConfig(order, "CloseAuction", "SendTimeOffsetSeconds", 23400);
        Trace.WriteLine($"Int Config: {intCfg}");
        order.Strategy = "TWAP";
        intCfg = configService.getIntConfig(order, "CloseAuction", "SendTimeOffsetSeconds", 23400);
        Trace.WriteLine($"Int Config: {intCfg}");

        //2.getIntConfig(IOrder order, "CloseAuction", "CancelSeconds", 23400)
        //    For a VWAP order it'll return 23400 as provided default (no row exists for this Cfg)
        //    For a TWAP order it'll return 23400 as provided default (no row exists for this Cfg)

        order = new Order()
        {
            Strategy = "VWAP",
            Aggression = "M",
            Country = "",
            AssetType = "",
            Account = "",
            TraderId = "Joe"
        };

        intCfg = configService.getIntConfig(order, "CloseAuction", "CancelSeconds", 23400);
        Trace.WriteLine($"Int Config: {intCfg}");
        order.Strategy = "TWAP";
        intCfg = configService.getIntConfig(order, "CloseAuction", "CancelSeconds", 23400);
        Trace.WriteLine($"Int Config: {intCfg}");

    }
}
