using Microsoft.Extensions.Configuration;

namespace StatMaster.Server;


//helper class to read the metric configuration from the configuration file
public static class MetricConfigReader //to klasa pomocnicza do odczytu konfiguracji metryk z pliku konfiguracyjnego
{
    public static List<MetricModel> ResolveEnabledItems(IConfiguration configuration)//this line reads the metric configuration from the configuration file
    {
        var items = configuration
            .GetSection("MetricCatalog:Items")
            .Get<List<MetricModel>>() ?? new List<MetricModel>(); //zaciagamy liste metric

        return items//filtrujemy
            .Where(i => i.Enabled) //enabled =true
            .GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase) //z kazdego elementu bierzemy key (np. system.hostname) i grupujemy po nim
            .Select(g => g.First()) //z kazdego grupy bierzemy pierwszy element np. system.hostname
            .ToList();//zwracam liste metric
    }

    public static string[] ResolveEnabledKeys(IConfiguration configuration)
        => ResolveEnabledItems(configuration)
            .Select(i => i.Key) //z kazdego elemetu bierzemy key (np. system.hostname)
            .ToArray();//zwracam tablice kluczy metryk
}
//Responsibility: Reads the metric configuration from the configuration file and returns the enabled keys.
//Why we need this class: To help the MetricScheduler class to get the enabled keys from the configuration file.