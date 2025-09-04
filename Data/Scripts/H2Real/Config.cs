using System;
using Sandbox.ModAPI;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    public class Config
    {
        public static string Version = "1.0.0";

        public string SYSTEM_VERSION = "1.0.0";
        public bool SYSTEM_AUTO_UPDATE = true;
        public float ICE_MELTING_ENERGY_PER_KG { get; set; } = 334000f; // W
        public float GAS_COMPRESSION_POWER_FULL_PER_LITER { get; set; } = 500f; // W
        public float ENERGY_PER_LITER { get; set; } = 1495.0f; // J/L
        public float H2_THRUST_EFFICIENCY { get; set; } = 0.65f; // Efficiency of H2 thrusters
        public float H2_ENGINE_EFFICIENCY { get; set; } = 0.65f; // Efficiency of H2 engines (typical ICE efficiency)
        public float H2_ENGINE_CRITICAL_TEMP { get; set; } = 300f;
        public float H2_THRUSTER_CRITICAL_TEMP { get; set; } = 500f;
        public float DAMAGE_PERCENT_ON_VERHEAT { get; set; } = .2f;
        private static Config _instance;
        private const string CONFIG_FILE = "TSUT_H2Real_Config.xml";

        public static Config Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Load();
                return _instance;
            }
        }

        public static Config Load()
        {
            Config config = new Config();
            if (MyAPIGateway.Utilities.FileExistsInWorldStorage(CONFIG_FILE, typeof(Config)))
            {
                try
                {
                    string contents;
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(CONFIG_FILE, typeof(Config)))
                    {
                        contents = reader.ReadToEnd();
                    }

                    // Check if version exists in the XML before deserializing
                    bool hasVersion = contents.Contains("<HEAT_SYSTEM_VERSION>");

                    config = MyAPIGateway.Utilities.SerializeFromXML<Config>(contents);

                    var defaultConfig = new Config();

                    var configUpdateNeeded = !hasVersion || config.SYSTEM_AUTO_UPDATE && config.SYSTEM_VERSION != defaultConfig.SYSTEM_VERSION;

                    MyLog.Default.WriteLine($"[H2Real] AutoUpdate: {config.SYSTEM_AUTO_UPDATE}, VersionMatches: {hasVersion && config.SYSTEM_VERSION == defaultConfig.SYSTEM_VERSION}, UpdateNeeded: {configUpdateNeeded}");

                    // Check version and auto-update if needed
                    if (configUpdateNeeded)
                    {
                        MyAPIGateway.Utilities.ShowMessage("H2Real", $"Config version mismatch. Auto-updating from {(hasVersion ? config.SYSTEM_VERSION : "Unknown")} to {defaultConfig.SYSTEM_VERSION}");
                        // Keep auto-update setting but reset everything else to defaults
                        bool autoUpdate = config.SYSTEM_AUTO_UPDATE;
                        config = new Config();
                        config.SYSTEM_AUTO_UPDATE = autoUpdate;
                        return config;
                    }
                }
                catch (Exception e)
                {
                    MyAPIGateway.Utilities.ShowMessage("H2Real", $"Failed to load config, using defaults. {e.Message}");
                }
            }

            return config;
        }

        public void Save()
        {
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(CONFIG_FILE, typeof(Config)))
                {
                    writer.Write(MyAPIGateway.Utilities.SerializeToXML(this));
                }
            }
            catch (Exception e)
            {
                MyLog.Default.Warning("H2Real", $"Failed to save config: {e.Message}");
            }
        }
    }
}