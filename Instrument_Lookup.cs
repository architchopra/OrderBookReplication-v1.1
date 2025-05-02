using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tt_net_sdk;
using System.Collections.Concurrent;

namespace Order_Book_Replication
{
    public delegate void LookupCompleteHandler(string Lookup_Category_Name, ConcurrentDictionary<string, tt_net_sdk.Instrument> instr_dict);
    public class Instrument_Lookup
    {
        private tt_net_sdk.Dispatcher dispatcher = null;
        private InstrumentCatalog OR_Catalog, QS_Catalog, ASE_Catalog;
        private InstrumentLookup OR_Lookup, QS_Lookup, ASE_Lookup;

        // Instrument Dictionaries:
        public ConcurrentDictionary<string, tt_net_sdk.Instrument> or_instruments = new ConcurrentDictionary<string, Instrument>();
        public ConcurrentDictionary<string, tt_net_sdk.Instrument> qs_instruments = new ConcurrentDictionary<string, Instrument>();
        public ConcurrentDictionary<string, tt_net_sdk.Instrument> ase_instruments = new ConcurrentDictionary<string, Instrument>();

        public event LookupCompleteHandler LookupHandler;

        List<string> or_prodNames = new List<string>() { "ZQ", "SR1", "SR3", "ESR" };
        List<string> qs_prodNames = new List<string>() { "ZQ", "SR1", "SR3", "SR1|ZQ", "ESR" };
        List<string> or_prodNames_ice = new List<string>() { "I", "ER3" };
        List<string> qs_prodNames_ice = new List<string>() { "I", "ER3", "I|ER3" };


        // Locks:
        private object or_lock = new object();
        private object qs_lock = new object();
        private object ase_lock = new object();

        public Instrument_Lookup() { }
        public Instrument_Lookup(tt_net_sdk.Dispatcher disp)
        {
            this.dispatcher = disp;
        }

        public void Start()
        {
            foreach (string ProdName in or_prodNames)
            {
                Log.Information("Starting OR CatOnData for ProdName: " + ProdName);
                OR_Catalog = new InstrumentCatalog(MarketId.CME, ProductType.Future, ProdName, this.dispatcher);
                OR_Catalog.OnData += new EventHandler<InstrumentCatalogEventArgs>(OR_Catalog_OnData);
                OR_Catalog.GetAsync();
            }

            foreach (string ProdName in qs_prodNames)
            {
                Log.Information("Starting QS CatOnData for ProdName: " + ProdName);
                QS_Catalog = new InstrumentCatalog(MarketId.CME, ProductType.MultilegInstrument, ProdName, this.dispatcher);
                QS_Catalog.OnData += new EventHandler<InstrumentCatalogEventArgs>(QS_Catalog_OnData);
                QS_Catalog.GetAsync();
            }
            
            foreach (string ProdName in or_prodNames_ice)
            {
                Log.Information("Starting OR CatOnData for ProdName: " + ProdName);
                OR_Catalog = new InstrumentCatalog(MarketId.ICEL, ProductType.Future, ProdName, this.dispatcher);
                OR_Catalog.OnData += new EventHandler<InstrumentCatalogEventArgs>(OR_Catalog_OnData);
                OR_Catalog.GetAsync();
            }

            foreach (string ProdName in qs_prodNames_ice)
            {
                Log.Information("Starting QS CatOnData for ProdName: " + ProdName);
                QS_Catalog = new InstrumentCatalog(MarketId.ICEL, ProductType.MultilegInstrument, ProdName, this.dispatcher);
                QS_Catalog.OnData += new EventHandler<InstrumentCatalogEventArgs>(QS_Catalog_OnData);
                QS_Catalog.GetAsync();
            }

            ASE_Catalog = new InstrumentCatalog(MarketId.ASE, ProductType.Synthetic, "ASE", this.dispatcher);
            ASE_Catalog.OnData += new EventHandler<InstrumentCatalogEventArgs>(ASE_Catalog_OnData);
            ASE_Catalog.GetAsync();
        }

        private void OR_Catalog_OnData(object sender, InstrumentCatalogEventArgs e)
        {
            if (e.Event == ProductDataEvent.Found)
            {
                Log.Information("Inside OR Catalog OnData");
                foreach (KeyValuePair<InstrumentKey, Instrument> kvp in e.InstrumentCatalog.Instruments)
                {
                    string m_alias = kvp.Key.ToString();
                    m_alias = m_alias.Substring(4);

                    OR_Lookup = new InstrumentLookup(this.dispatcher, kvp.Key.MarketId, kvp.Value.Product.Type, kvp.Value.Product.Name, m_alias);
                    OR_Lookup.OnData += OR_Lookup_OnData;
                    OR_Lookup.GetAsync();
                }
            }
            else
            {
                Log.Information("OR Catalog Not Found - Event Code: " + e.Event+ e.InstrumentCatalog.ProductName+e.InstrumentCatalog.Product);
            }
        }
        private void QS_Catalog_OnData(object sender, InstrumentCatalogEventArgs e)
        {
            if (e.Event == ProductDataEvent.Found)
            {
                Log.Information("Inside QS Catalog OnData");
                foreach (KeyValuePair<InstrumentKey, Instrument> kvp in e.InstrumentCatalog.Instruments)
                {
                    string m_alias = kvp.Key.ToString();
                    m_alias = m_alias.Substring(4);

                    QS_Lookup = new InstrumentLookup(this.dispatcher, kvp.Key.MarketId, kvp.Value.Product.Type, kvp.Value.Product.Name, m_alias);
                    QS_Lookup.OnData += QS_Lookup_OnData;
                    QS_Lookup.GetAsync();
                }
            }
            else
            {
                Log.Information("QS Catalog Not Found - Event Code: " + e.Event+e.InstrumentCatalog.ProductName+e.InstrumentCatalog.Product);
            }
        }
        private void ASE_Catalog_OnData(object sender, InstrumentCatalogEventArgs e)
        {
            if (e.Event == ProductDataEvent.Found)
            {
                Log.Information("Inside ASE Catalog OnData");
                foreach (KeyValuePair<InstrumentKey, Instrument> kvp in e.InstrumentCatalog.Instruments)
                {
                    string m_alias = kvp.Key.ToString();
                    m_alias = m_alias.Substring(4);

                    ASE_Lookup = new InstrumentLookup(this.dispatcher, kvp.Key.MarketId, kvp.Value.Product.Type, kvp.Value.Product.Name, m_alias);
                    ASE_Lookup.OnData += ASE_Lookup_OnData;
                    ASE_Lookup.GetAsync();
                }
            }
            else
            {
                Log.Information("OR Catalog Not Found - Event Code: " + e.Event);
            }
        }

        private void OR_Lookup_OnData(object sender, InstrumentLookupEventArgs e)
        {
            lock (or_lock)
            {
                if (e.Event == ProductDataEvent.Found)
                {
                    Log.Information("OR Instrument Found: " + e.InstrumentLookup.Instrument.InstrumentDetails.Alias);

                    this.or_instruments.TryAdd(e.InstrumentLookup.Instrument.InstrumentDetails.Alias, e.InstrumentLookup.Instrument);

                    this.LookupHandler("OR", this.or_instruments);
                }
                else
                {
                    Log.Information("OR Lookup Not Done - Event Code: " + e.Event);
                }
            }
        }
        private void QS_Lookup_OnData(object sender, InstrumentLookupEventArgs e)
        {
            lock (qs_lock)
            {
                if (e.Event == ProductDataEvent.Found)
                {
                    Log.Information("QS Instrument Found: " + e.InstrumentLookup.Instrument.InstrumentDetails.Alias);

                    this.qs_instruments.TryAdd(e.InstrumentLookup.Instrument.InstrumentDetails.Alias, e.InstrumentLookup.Instrument);

                    this.LookupHandler("QS", this.qs_instruments);
                }
                else
                {
                    Log.Information("QS Lookup Not Done - Event Code: " + e.Event);
                }
            }
        }
        private void ASE_Lookup_OnData(object sender, InstrumentLookupEventArgs e)
        {
            lock (ase_lock)
            {
                if (e.Event == ProductDataEvent.Found)
                {
                    Log.Information("ASE Instrument Found: " + e.InstrumentLookup.Instrument.InstrumentDetails.Alias);
                    if (this.ase_instruments.ContainsKey(e.InstrumentLookup.Instrument.InstrumentDetails.Alias))
                    {

                    }
                    else
                    {
                        this.ase_instruments.TryAdd(e.InstrumentLookup.Instrument.InstrumentDetails.Alias, e.InstrumentLookup.Instrument);
                    }

                    this.LookupHandler("ASE", this.ase_instruments);
                }
                else
                {
                    Log.Information("ASE Lookup Not Done - Event Code: " + e.Event);
                }
            }
        }
    }
}
