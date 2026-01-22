using DWIS.Client.ReferenceImplementation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DWIS.API.DTO;

namespace DWIS.AdviceComposer.Service
{
    internal class Entry
    {
        public string sparql { get; set; } = string.Empty;
        public Guid Key { get; set; } = Guid.Empty;
        public List<QueryResultRow> Results { get; set; } = new List<QueryResultRow>();
        public Dictionary<Guid, LiveValue> LiveValues { get; set; } = new Dictionary<Guid, LiveValue>();
    }

    internal class LiveValue
    {
        public string ns { get; set; } = string.Empty;
        public string id { get; set; } = string.Empty;
        public object? val { get; set; } = null;
        public DateTime Timestamp { get; set; } = DateTime.MinValue;

        public LiveValue(): base() {}

        public LiveValue(string ns, string id, object? val)
        {
            this.ns = ns;
            this.id = id;
            this.val = val;
            this.Timestamp = DateTime.MinValue;
        }
    }
}
