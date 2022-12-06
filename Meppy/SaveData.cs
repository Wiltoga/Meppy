using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Wiltoga.Meppy
{
    public class Data
    {
        public Data()
        {
            Rules = Array.Empty<Rule>();
        }

        public Rule[] Rules { get; set; }
    }

    public class Rule
    {
        public Rule()
        {
            ProcessName = "";
        }

        public string ProcessName { get; set; }
        public State? State { get; set; }
    }

    public class State
    {
        [JsonIgnore]
        public State Copy => new State
        {
            Height = Height,
            Left = Left,
            Top = Top,
            Width = Width,
            WindowState = WindowState
        };

        public int Height { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public Win32.ShowWindowCommands WindowState { get; set; }
    }

    internal static class SaveData
    {
        private static FileInfo SaveFile => new FileInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Meppy", "config.json"));

        public static Data LoadRules()
        {
            Data? data = null;
            if (SaveFile.Exists)
            {
                data = JsonSerializer.Deserialize<Data>(File.ReadAllText(SaveFile.FullName, Encoding.UTF8));
            }
            data ??= new Data();

            if (data.Rules is null)
                data.Rules = Array.Empty<Rule>();

            return data;
        }

        public static void SaveRules(Data data)
        {
            SaveFile.Directory?.Create();
            var json = JsonSerializer.Serialize(data);
            File.WriteAllText(SaveFile.FullName, json, Encoding.UTF8);
        }
    }
}