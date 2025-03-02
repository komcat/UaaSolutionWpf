using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// Model classes for RealTimeData.json
public class RealTimeDataModel
{
    public List<RealTimeDataChannel> Channels { get; set; }
    public GlobalSettings GlobalSettings { get; set; }
}

public class RealTimeDataChannel
{
    public string ChannelName { get; set; }
    public int Id { get; set; }
    public double Value { get; set; }
    public string Unit { get; set; }
    public double Target { get; set; }
}

public class GlobalSettings
{
    public string ApplicationName { get; set; }
    public string Version { get; set; }
    public int UpdateInterval { get; set; }
}
