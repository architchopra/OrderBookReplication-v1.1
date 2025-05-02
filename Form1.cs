using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.IO;
using tt_net_sdk;
using Serilog;
using CsvHelper;
using CsvHelper.Configuration.Attributes;
using CsvHelper.Configuration;
using System.Globalization;
using System.Data;
using tt.messaging.order.enums;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Timers;

namespace Order_Book_Replication
{
    public partial class Form1 : Form
    {
        private TTAPI m_api = null;
        private tt_net_sdk.WorkerDispatcher m_disp = null;
        private tt_net_sdk.Dispatcher trade_disp = null;
        private bool m_isOrderBookDownloaded = false;
        private bool m_isOrdersSynced = false;
        private IReadOnlyCollection<Account> m_accounts = null;
        private TradeSubscription m_instrumentTradeSubscription = null;
        TradeSubscriptionTTAccountFilter tsiAF;
        private object m_Lock = new object();
        private bool m_isDisposed = false;
        private bool trade_sub_started = false;

        private tt_net_sdk.Instrument instr = null;

        //private readonly int account_idx = 17; // Enter your Account Index
        private readonly int account_idx_cme = 0; // Enter your Account Index
        private readonly int account_idx_ice = 0; // Enter your Account Index
                                                  //private readonly int account_idx = 1; // Enter your Account Index
                                                  //private readonly string account_name = "XGPPL-SIM"; // Enter your Account In
        /*  private readonly string account_name = "JRathore-SIM"; // Enter your Account In*/
        private readonly string account_name = "JRathore-SIM"; // Enter your Account In
                                                               //private readonly string account_name = "XGMJN_CME"; // Enter your Account In
        /*    private readonly string account_name = "XGJRE_CME"; // Enter your Account In*/

        // Environment Type:
        private static string env_type = "SIM";
       /* private static string env_type = "LIVE";*/

        private string Log_File_Path = @"C:\tt\Order_Book_Replication\Logs_" + env_type + @"\LogFile_" + DateTime.Now.ToString("dd.MM.yyyy_HH.mm.ss.ffff") + ".log";

        // CSV Headers List:
        private readonly List<string> CSV_Headers = new List<string>() { "TimeStamp", "MarketID", "ProdType", "ProdName", "Alias", "BuySell", "OrderType", "LimitPrice",
                                                                         "OrderQuantity", "FilledQuantity", "WorkingQuantity", "TimeInForce", "TextA", "SiteOrderKey" };

        // OrderBook CSV File Path:
        private string CSV_File_Path = null;

        // Checkbox Bools:
        private bool send_or = false;
        private bool send_qs = false;
        private bool send_ase = false;

        // Lookup Complete Bools:
        private bool or_lookup_done = false;
        private bool qs_lookup_done = false;
        private bool ase_lookup_done = false;

        // Lookup Dicts:
        // ASE lookup Dictionaries:
        public ConcurrentDictionary<string, tt_net_sdk.Instrument> or_instruments = null;
        public ConcurrentDictionary<string, tt_net_sdk.Instrument> qs_instruments = null;
        public ConcurrentDictionary<string, tt_net_sdk.Instrument> ase_instruments = null;

        // Orders Dictionaries:
        private ConcurrentDictionary<string, Orders_Data_1> or_orders_dict = new ConcurrentDictionary<string, Orders_Data_1>();
        private ConcurrentDictionary<string, Orders_Data_1> qs_orders_dict = new ConcurrentDictionary<string, Orders_Data_1>();
        private ConcurrentDictionary<string, Orders_Data_1> ase_orders_dict = new ConcurrentDictionary<string, Orders_Data_1>();
        ConcurrentDictionary<string, Orders_Data_1> dict3 = new ConcurrentDictionary<string, Orders_Data_1>();
        System.Timers.Timer m_timer = null;
        // Instrument Lookup Class:
        private Instrument_Lookup IL = null;

        //Price Subscribers
        PriceSubscriber sub1 = null;
        PriceSubscriber sub2 = null;
        PriceUpdateService updateService = null;


        private string Market_Orders = @"C:\tt\order_replication_dets\market_order_CME.csv";


        DateTime dt_check=DateTime.Now.AddYears(100);
        public Form1()
        {
            InitializeComponent();
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            panel1.Hide();
            button2.Hide();
            button3.Hide();
            button4.Hide();
            richTextBox2.Hide();
            checkBox1.Hide();
            checkBox2.Hide();
            checkBox3.Hide();
            richTextBox1.Text = "File Reading Updates:\n";
        }
        public void Start(tt_net_sdk.TTAPIOptions apiConfig)
        {
            m_disp = tt_net_sdk.Dispatcher.AttachWorkerDispatcher();
            m_disp.DispatchAction(() =>
            {
                Init(apiConfig);
            });

            m_disp.Run();
        }
        public void Init(tt_net_sdk.TTAPIOptions apiConfig)
        {
            ApiInitializeHandler apiInitializeHandler = new ApiInitializeHandler(ttNetApiInitHandler);
            TTAPI.ShutdownCompleted += TTAPI_ShutdownCompleted;
            TTAPI.CreateTTAPI(tt_net_sdk.Dispatcher.Current, apiConfig, apiInitializeHandler);
        }
        public void ttNetApiInitHandler(TTAPI api, ApiCreationException ex)
        {
            try
            {
                if (ex == null)
                {
                    // Configuring logger:
                    Log.Logger = new LoggerConfiguration()
                        .WriteTo.File(Log_File_Path)
                        .WriteTo.Console()
                        .CreateLogger();

                    Log.Information("TT.NET SDK INITIALIZED : Start Time: {0}", DateTime.Now);
                    Log.Information("Environmant Type: " + env_type);

                    // Authenticate your credentials
                    m_api = api;
                    m_api.TTAPIStatusUpdate += new EventHandler<TTAPIStatusUpdateEventArgs>(m_api_TTAPIStatusUpdate);
                    m_api.OrderbookSynced += M_api_OrderbookSynced;
                    m_api.Start();
                }
                else if (ex.IsRecoverable)
                {
                    // this is in informational update from the SDK
                    Log.Information("TT.NET SDK Initialization Message: {0} , At Time: {1}", ex.Message, DateTime.Now);

                    if (ex.Code == ApiCreationException.ApiCreationError.NewAPIVersionAvailable)
                    {
                        // a newer version of the SDK is available - notify someone to upgrade
                    }
                }
                else
                {
                    Log.Error("TT.NET SDK Initialization Failed: {0} , At Time: {1}", ex.Message, DateTime.Now);
                    if (ex.Code == ApiCreationException.ApiCreationError.NewAPIVersionRequired)
                    {
                        // do something to upgrade the SDK package since it will not start until it is upgraded 
                        // to the minimum version noted in the exception message
                    }
                    Dispose();
                }
            }
            catch (Exception exc)
            {
                Log.Error("Exception: " + exc.Message);
            }
        }
        public void m_api_TTAPIStatusUpdate(object sender, TTAPIStatusUpdateEventArgs e)
        {
            try
            {
                Log.Information("TTAPIStatusUpdate: {0}", e);

                if (e.IsReady == false)
                {
                    // TODO: Do any connection lost processing here
                    return;
                }
                updateService = new PriceUpdateService(tt_net_sdk.Dispatcher.Current);

                // Get the accounts
                m_accounts = m_api.Accounts;
                foreach (var account in m_accounts)
                {
                    Log.Information(account.ToString());
                }
             /*   Log.Information("Account at Idx " + account_idx_cme + ": " + m_accounts.ElementAt(account_idx).ToString());*/

                // Starting Instrument Lookup:
                IL = new Instrument_Lookup(tt_net_sdk.Dispatcher.Current);
                Thread newThreadIL = new Thread(() => IL.Start());
                newThreadIL.Name = "InstrumentLookupThread";
                newThreadIL.Start();
                IL.LookupHandler += new LookupCompleteHandler(Lookup_Completed_Handler);
                m_timer = new System.Timers.Timer()
                {
                    Interval = 5000,
                    Enabled = true,
                    AutoReset = true
                };
                m_timer.Elapsed += new ElapsedEventHandler(m_handler);
                m_timer.Start();
                // Saving Dispather for TS:
                trade_disp = tt_net_sdk.Dispatcher.Current;
            }
            catch (Exception exc)
            {
                Log.Error("Exception: " + exc.Message);
            }
        }

        private void M_api_OrderbookSynced(object sender, EventArgs e)
        {
            Log.Information("Orderbook Synced...");
            m_isOrdersSynced = true;
        }
        private void m_handler(object sender, EventArgs e)
        {
            if ((DateTime.Now - dt_check).TotalSeconds > 120)
            {
                Write_market_orders(Market_Orders);
            }
        }
        private void Start_TradeSubscription()
        {
            // Starting TradeSubscription and Instrument Lookup:
            // Creting TradeSubscription:
            // Create a TradeSubscription to listen for order / fill events only for orders submitted through it
            m_instrumentTradeSubscription = new TradeSubscription(trade_disp);
            m_instrumentTradeSubscription.Start();

            panel1.Show();
        }
        public bool HandlePriceUpdate3(PriceSubscriber subscriber, bool buy, Price pr, ConcurrentDictionary<string, Orders_Data_1> dict, string order_key, string dict_name, TradeSubscription trade_subscription, IReadOnlyCollection<Account> m_account, int acc_idx, Task[] taskArray, int i, PriceUpdateService updateService)
        {
            bool chcked = false;
            try
            {

                Instrument symbol = subscriber.Symbol();
                PricePublisher publisher = subscriber.GetLatestPriceUpdatePublisher();
                ManualResetEvent waitHandle = publisher.GetPriceChangedEvent();

                while (waitHandle.WaitOne())
                {
                    PriceUpdate latestPriceUpdate = subscriber.GetLatestPriceUpdate();
                    int ticks = Math.Abs(latestPriceUpdate.AskPrice.ToTicks() - latestPriceUpdate.BidPrice.ToTicks());
                    if (buy)
                    {
                        if (Convert.ToDecimal(pr) < Convert.ToDecimal(latestPriceUpdate.AskPrice) && Convert.ToDecimal(pr) <= Convert.ToDecimal(latestPriceUpdate.BidPrice + (ticks/2)))
                        {
                           
                            taskArray[i] = Task.Run(() => this.Send_Order_Reload_Check(dict, order_key, dict_name, trade_subscription, m_account, acc_idx));
                            updateService.Unsubscribe(dict[order_key].Instrument, subscriber);

                        }
                        else
                        {
                            dict3.TryAdd(order_key, dict[order_key]);

                        }
                    }
                    else
                    {
                        if (Convert.ToDecimal(pr) > Convert.ToDecimal(latestPriceUpdate.BidPrice) && Convert.ToDecimal(pr) > Convert.ToDecimal(latestPriceUpdate.AskPrice - (ticks / 2)))
                        {
                           
                            taskArray[i] = Task.Run(() => this.Send_Order_Reload_Check(dict, order_key, dict_name, trade_subscription, m_account, acc_idx));
                            updateService.Unsubscribe(dict[order_key].Instrument, subscriber);
                        }
                        else
                        {
                            dict3.TryAdd(order_key, dict[order_key]);

                        }
                    }
                    break;
                  
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("exited");
            }
            return chcked;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            button2.Hide();
            button3.Hide();
            button4.Hide();
            richTextBox2.Hide();
            checkBox1.Hide();
            checkBox2.Hide();
            checkBox3.Hide();

            OpenFileDialog openFileDialog1 = new OpenFileDialog
            {
                InitialDirectory = @"D:\",
                Title = "Browse Text Files",

                CheckFileExists = true,
                CheckPathExists = true,

                DefaultExt = "csv",
                Filter = "csv files (*.csv)|*.csv",
                FilterIndex = 2,
                RestoreDirectory = true,

                ReadOnlyChecked = true,
                ShowReadOnly = true
            };

            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                textBox1.Text = openFileDialog1.FileName;
                CSV_File_Path = openFileDialog1.FileName;

                button2.Show();
            }
        }
        private void button2_Click(object sender, EventArgs e)
        {
            button1.Hide();
            DialogResult dialogResult = MessageBox.Show("Read the selected file?", "Action confirmation", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                // Testing If there are correct headers in the CSV:
                CsvConfiguration csvConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    Delimiter = ","
                };
                // Read column headers from file
                CsvReader csv1 = new CsvReader(File.OpenText(CSV_File_Path), csvConfiguration);
                csv1.Read();
                csv1.ReadHeader();
                List<string> headers = csv1.HeaderRecord.ToList();

                bool headers_matched = false;

                if (headers.Count == CSV_Headers.Count)
                {
                    for (int i = 0; i < headers.Count; i++)
                    {
                        if (headers[i] != CSV_Headers[i])
                        {
                            headers_matched = false;
                        }
                        else
                        {
                            headers_matched = true;
                        }
                    }
                }
                else
                {
                    headers_matched = false;
                }
                csv1.Dispose();

                if (headers_matched)
                {
                    Log.Information("Headers Matched");

                    or_orders_dict.Clear();
                    qs_orders_dict.Clear();
                    ase_orders_dict.Clear();

                    Read_File_OR("OR", or_instruments);
                    Read_File_QS("QS", qs_instruments);
                    Read_File_ASE("ASE", ase_instruments);
                }
                else
                {
                    button1.Show();
                    MessageBox.Show("Column Headers Do no match the required format.\nKindly use a different CSV file.");
                }
            }
            else if (dialogResult == DialogResult.No)
            {
                button1.Show();
            }
        }
        private void button3_Click(object sender, EventArgs e)
        {
            Log.Information("Routing Orders");
            Task.Run(() =>
            {
                if (send_or)
                {

                    Send_Orders_Task_Creator(or_orders_dict, "OR", m_instrumentTradeSubscription, m_accounts, updateService);

                }
                if (send_qs)
                {
                    Send_Orders_Task_Creator(qs_orders_dict, "QS", m_instrumentTradeSubscription, m_accounts, updateService);


                }
                if (send_ase)
                {
                    Send_Orders_Task_Creator(ase_orders_dict, "ASE", m_instrumentTradeSubscription, m_accounts, updateService);
                }
            });
           
            dt_check=DateTime.Now;
        }
        private void button4_Click(object sender, EventArgs e)
        {
            Log.Information("OR Data:");
            Log.Information("OR Dict Count:" + or_orders_dict.Count);
            foreach (var data in or_orders_dict)
            {
                Log.Information("Alias: " + data.Value.Instrument.Key.Alias + " BuySell: " + data.Value.BuySell + " OrderType: " + data.Value.OrderType + "LimitPrice: " + data.Value.LimitPrice +
                                " OrderQuantity: " + data.Value.OrderQuantity + " FilledQuantity: " + data.Value.FilledQuantity + "\nWorkingQuantity: " + data.Value.WorkingQuantity +
                                " TimeInForce: " + data.Value.TimeInForce + " SiteOrderKey: " + data.Value.SiteOrderKey);
            }

            Log.Information("QS Data:");
            Log.Information("QS Dict Count:" + qs_orders_dict.Count);
            foreach (var data in qs_orders_dict)
            {
                Log.Information("Alias: " + data.Value.Instrument.Key.Alias + " BuySell: " + data.Value.BuySell + " OrderType: " + data.Value.OrderType + "LimitPrice: " + data.Value.LimitPrice +
                                " OrderQuantity: " + data.Value.OrderQuantity + " FilledQuantity: " + data.Value.FilledQuantity + "\nWorkingQuantity: " + data.Value.WorkingQuantity +
                                " TimeInForce: " + data.Value.TimeInForce + " SiteOrderKey: " + data.Value.SiteOrderKey);
            }
            
            Log.Information("ASE Data:");
            Log.Information("ASE Dict Count:" + ase_orders_dict.Count);
            foreach (var data in ase_orders_dict)
            {
                Log.Information("Alias: " + data.Value.Instrument.Key.Alias + " BuySell: " + data.Value.BuySell + " OrderType: " + data.Value.OrderType + "LimitPrice: " + data.Value.LimitPrice +
                                " OrderQuantity: " + data.Value.OrderQuantity + " FilledQuantity: " + data.Value.FilledQuantity + "\nWorkingQuantity: " + data.Value.WorkingQuantity +
                                " TimeInForce: " + data.Value.TimeInForce + " SiteOrderKey: " + data.Value.SiteOrderKey);
            }
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox1.Checked)
            {
                send_or = true;
            }
            else
            {
                send_or = false;
            }
        }
        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox2.Checked)
            {
                send_qs = true;
            }
            else
            {
                send_qs = false;
            }
        }
        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox3.Checked)
            {
                send_ase = true;
            }
            else
            {
                send_ase = false;
            }
        }
        public void Write_market_orders(String file_name)
        {
            Log.Information("Storing Market_Orders In File: " + file_name);
            System.IO.FileInfo file = new System.IO.FileInfo(file_name);
            file.Directory.Create();
            var config = new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture);
            using (var stream = File.Open(file_name, FileMode.Create, FileAccess.Write, FileShare.Write))
            using (var writer = new StreamWriter(stream))
            using (var csv = new CsvWriter(writer, config))
            {
                csv.WriteHeader<Orders_Data>();
                csv.NextRecord();
                decimal d_qty = 0;
               
                foreach (KeyValuePair<string, Orders_Data_1> kvp in dict3)
                {
                    if (!kvp.Value.WorkingQuantity.IsValid)
                    {
                        d_qty = kvp.Value.OrderQuantity.Value;
                    }
                    else
                    {
                        d_qty = kvp.Value.WorkingQuantity.Value;

                    }
                    Orders_Data orders_Data = new Orders_Data()
                    {
                        TimeStamp = DateTime.Now,
                        MarketID = kvp.Value.Instrument.Product.Market.MarketId.ToString(),
                        ProdType = kvp.Value.Instrument.Product.Type.ToString(),
                        ProdName = kvp.Value.Instrument.Product.Name,
                        Alias = kvp.Value.Instrument.InstrumentDetails.Alias,
                        BuySell = kvp.Value.BuySell.ToString(),
                        OrderType = kvp.Value.OrderType.ToString(),
                        LimitPrice = kvp.Value.LimitPrice.Value,
                        OrderQuantity = kvp.Value.OrderQuantity.Value,
                        FilledQuantity = kvp.Value.FilledQuantity,

                        WorkingQuantity = d_qty,
                        TimeInForce = kvp.Value.TimeInForce.ToString(),
                        TextA = kvp.Value.TextA,
                        SiteOrderKey = kvp.Value.SiteOrderKey,
                       
                    };

                    csv.WriteRecord(orders_Data);
                    csv.NextRecord();
                }
            }
            Environment.Exit(123);

        }
        private void Read_File_OR(string dict_name, ConcurrentDictionary<string, tt_net_sdk.Instrument> instr_dict)
        {
            // Reading File:
            var config = new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture);
            using (var stream = File.Open(CSV_File_Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            using (var csv = new CsvReader(reader, config))
            {
                string[] database = csv.HeaderRecord;

                var dataBase = csv.GetRecords<Orders_Data>();
                Log.Information("Reading CSV File for OR");
                richTextBox1.AppendText("\nReading CSV File for OR...");
                foreach (var data in dataBase)
                {
                    if (data.ProdType == "Future")
                    {
                        //Log.Information("Adding OR Orders to Dict");
                        Orders_Data_1 orders_Data_1 = new Orders_Data_1();

                        // Getting Instrument:
                        if (dict_name == "OR")
                        {
                            foreach (var instrData in instr_dict)
                            {
                                if (instrData.Key == data.Alias)
                                {
                                    this.instr = instrData.Value;
                                    Log.Information("Found OR Instrument: " + this.instr.Name);
                                    orders_Data_1.Instrument = this.instr;
                                }
                            }
                        }

                        // Checking Buysell:
                        if (data.BuySell == "Buy")
                        {
                            orders_Data_1.BuySell = BuySell.Buy;
                        }
                        else
                        {
                            orders_Data_1.BuySell = BuySell.Sell;
                        }

                        // Checking OrderType:
                        if (data.OrderType == "Limit")
                        {
                            orders_Data_1.OrderType = OrderType.Limit;
                        }
                        else if (data.OrderType == "Market")
                        {
                            orders_Data_1.OrderType = OrderType.Market;
                        }
                        else if (data.OrderType == "StopLimit")
                        {
                            orders_Data_1.OrderType = OrderType.StopLimit;
                        }
                        else if (data.OrderType == "Stop")
                        {
                            orders_Data_1.OrderType = OrderType.Stop;
                        }
                        else if (data.OrderType == "StopMarketToLimit")
                        {
                            orders_Data_1.OrderType = OrderType.StopMarketToLimit;
                        }
                        else if (data.OrderType == "MarketCloseToday")
                        {
                            orders_Data_1.OrderType = OrderType.MarketCloseToday;
                        }
                        else if (data.OrderType == "NotSet")
                        {
                            orders_Data_1.OrderType = OrderType.NotSet;
                        }
                        else
                        {
                            orders_Data_1.OrderType = OrderType.NotSet;
                        }

                        // Adding Price:
                        orders_Data_1.LimitPrice = Price.FromDecimal(instr, (data.LimitPrice*100));

                        // Adding OrderQuamtity:
                        orders_Data_1.OrderQuantity = Quantity.FromDecimal(instr, data.OrderQuantity);

                        // Adding FillQuamtity:
                        orders_Data_1.FilledQuantity = Quantity.FromDecimal(instr, data.FilledQuantity);

                        // Adding WorkingQuamtity:
                        orders_Data_1.WorkingQuantity = Quantity.FromDecimal(instr, data.WorkingQuantity);

                        // Checking TIF:
                        if (data.TimeInForce == tt_net_sdk.TimeInForce.GoodTillCancel.ToString())
                        {
                            orders_Data_1.TimeInForce = tt_net_sdk.TimeInForce.GoodTillCancel;
                        }
                        else if (data.TimeInForce == tt_net_sdk.TimeInForce.Day.ToString())
                        {
                            orders_Data_1.TimeInForce = tt_net_sdk.TimeInForce.Day;
                        }
                        else if (data.TimeInForce == tt_net_sdk.TimeInForce.FillOrKill.ToString())
                        {
                            orders_Data_1.TimeInForce = tt_net_sdk.TimeInForce.FillOrKill;
                        }
                        else
                        {
                            orders_Data_1.TimeInForce = tt_net_sdk.TimeInForce.GoodTillCancel;
                        }

                        // Adding TextA:
                        orders_Data_1.TextA = data.TextA;

                        // Adding SiteOrderKey:
                        orders_Data_1.SiteOrderKey = data.SiteOrderKey;

                        // Storing in Dictionary:
                        Console.WriteLine("Inst Not Null: " + (orders_Data_1.Instrument != null).ToString());
                        Console.WriteLine("Inst Name: " + orders_Data_1.Instrument.Key.Alias.ToString());
                        Console.WriteLine("Price: " + orders_Data_1.LimitPrice.ToString() + " IsValid: " + orders_Data_1.LimitPrice.IsValid);
                        Console.WriteLine("OrdQty: " + orders_Data_1.OrderQuantity.ToString() + " IsValid: " + orders_Data_1.OrderQuantity.IsValid);
                        Console.WriteLine("WorkQty: " + orders_Data_1.WorkingQuantity.ToString() + " IsValid: " + orders_Data_1.WorkingQuantity.IsValid);
                        Console.WriteLine("FillQty: " + orders_Data_1.FilledQuantity.ToString() + " IsValid: " + orders_Data_1.FilledQuantity.IsValid);
                        if (orders_Data_1.LimitPrice.IsValid & orders_Data_1.OrderQuantity.IsValid & orders_Data_1.WorkingQuantity.IsValid & orders_Data_1.FilledQuantity.IsValid)
                        {
                            if (!or_orders_dict.ContainsKey(data.SiteOrderKey))
                            {
                                or_orders_dict.TryAdd(data.SiteOrderKey, orders_Data_1);
                            }
                            //Log.Information("Added Order Details in Dict");
                        }
                        else
                        {
                            /*Console.WriteLine(orders_Data_1.Instrument.Name.ToString());
                            Console.WriteLine(orders_Data_1.BuySell.ToString());
                            Console.WriteLine(orders_Data_1.LimitPrice.ToString());
                            Console.WriteLine(orders_Data_1.OrderQuantity.ToString());
                            Console.WriteLine(orders_Data_1.WorkingQuantity.ToString());
                            Console.WriteLine(orders_Data_1.SiteOrderKey.ToString());*/
                            MessageBox.Show(string.Format("Data Invalid for: " + orders_Data_1.Instrument.Key.Alias.ToString() + " " + orders_Data_1.BuySell.ToString() + " " + orders_Data_1.LimitPrice.ToString()
                                 + " " + orders_Data_1.OrderQuantity.ToString() + " " + orders_Data_1.WorkingQuantity.ToString() + " " + orders_Data_1.SiteOrderKey.ToString()));
                        }
                    }
                }

                richTextBox1.AppendText("\nFile Read Complete for OR!");
                button1.Show();
                button3.Show();
                button4.Show();
                richTextBox2.Show();
                checkBox1.Show();
                checkBox2.Show();
                checkBox3.Show();
            }
        }
        private void Read_File_QS(string dict_name, ConcurrentDictionary<string, tt_net_sdk.Instrument> instr_dict)
        {
            // Reading File:
            var config = new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture);
            using (var stream = File.Open(CSV_File_Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            using (var csv = new CsvReader(reader, config))
            {
                string[] database = csv.HeaderRecord;

                var dataBase = csv.GetRecords<Orders_Data>();
                Log.Information("Reading CSV File for QS");
                richTextBox1.AppendText("\nReading CSV File for QS...");
                foreach (var data in dataBase)
                {
                    if (data.ProdType == "MultilegInstrument")
                    {
                        //Log.Information("Adding QS Orders to Dict");
                        Orders_Data_1 orders_Data_1 = new Orders_Data_1();

                        // Getting Instrument:
                        if (dict_name == "QS")
                        {
                            foreach (var instrData in instr_dict)
                            {
                                if (instrData.Key == data.Alias)
                                {
                                    this.instr = instrData.Value;
                                    Log.Information("Found QS Instrument: " + this.instr.Name);
                                    orders_Data_1.Instrument = this.instr;
                                }
                            }
                        }

                        // Checking Buysell:
                        if (data.BuySell == "Buy")
                        {
                            orders_Data_1.BuySell = BuySell.Buy;
                        }
                        else
                        {
                            orders_Data_1.BuySell = BuySell.Sell;
                        }

                        // Checking OrderType:
                        if (data.OrderType == "Limit")
                        {
                            orders_Data_1.OrderType = OrderType.Limit;
                        }
                        else if (data.OrderType == "Market")
                        {
                            orders_Data_1.OrderType = OrderType.Market;
                        }
                        else if (data.OrderType == "StopLimit")
                        {
                            orders_Data_1.OrderType = OrderType.StopLimit;
                        }
                        else if (data.OrderType == "Stop")
                        {
                            orders_Data_1.OrderType = OrderType.Stop;
                        }
                        else if (data.OrderType == "StopMarketToLimit")
                        {
                            orders_Data_1.OrderType = OrderType.StopMarketToLimit;
                        }
                        else if (data.OrderType == "MarketCloseToday")
                        {
                            orders_Data_1.OrderType = OrderType.MarketCloseToday;
                        }
                        else if (data.OrderType == "NotSet")
                        {
                            orders_Data_1.OrderType = OrderType.NotSet;
                        }
                        else
                        {
                            orders_Data_1.OrderType = OrderType.NotSet;
                        }

                        // Adding Price:
                        orders_Data_1.LimitPrice = Price.FromDecimal(instr, data.LimitPrice);

                        // Adding OrderQuamtity:
                        orders_Data_1.OrderQuantity = Quantity.FromDecimal(instr, data.OrderQuantity);

                        // Adding FillQuamtity:
                        orders_Data_1.FilledQuantity = Quantity.FromDecimal(instr, data.FilledQuantity);

                        // Adding WorkingQuamtity:
                        orders_Data_1.WorkingQuantity = Quantity.FromDecimal(instr, data.WorkingQuantity);

                        // Checking TIF:
                        if (data.TimeInForce == tt_net_sdk.TimeInForce.GoodTillCancel.ToString())
                        {
                            orders_Data_1.TimeInForce = tt_net_sdk.TimeInForce.GoodTillCancel;
                        }
                        else if (data.TimeInForce == tt_net_sdk.TimeInForce.Day.ToString())
                        {
                            orders_Data_1.TimeInForce = tt_net_sdk.TimeInForce.Day;
                        }
                        else if (data.TimeInForce == tt_net_sdk.TimeInForce.FillOrKill.ToString())
                        {
                            orders_Data_1.TimeInForce = tt_net_sdk.TimeInForce.FillOrKill;
                        }
                        else
                        {
                            orders_Data_1.TimeInForce = tt_net_sdk.TimeInForce.GoodTillCancel;
                        }

                        // Adding TextA:
                        orders_Data_1.TextA = data.TextA;

                        // Adding SiteOrderKey:
                        orders_Data_1.SiteOrderKey = data.SiteOrderKey;

                        // Storing in Dictionary:
                        /*Console.WriteLine("Price: " + orders_Data_1.LimitPrice.ToString() + " IsValid: " + orders_Data_1.LimitPrice.IsValid);
                        Console.WriteLine("OrdQty: " + orders_Data_1.OrderQuantity.ToString() + " IsValid: " + orders_Data_1.OrderQuantity.IsValid);
                        Console.WriteLine("WorkQty: " + orders_Data_1.WorkingQuantity.ToString() + " IsValid: " + orders_Data_1.WorkingQuantity.IsValid);
                        Console.WriteLine("FillQty: " + orders_Data_1.FilledQuantity.ToString() + " IsValid: " + orders_Data_1.FilledQuantity.IsValid);*/
                        if (orders_Data_1.LimitPrice.IsValid & orders_Data_1.OrderQuantity.IsValid & orders_Data_1.WorkingQuantity.IsValid & orders_Data_1.FilledQuantity.IsValid)
                        {
                            if (!qs_orders_dict.ContainsKey(data.SiteOrderKey))
                            {
                                qs_orders_dict.TryAdd(data.SiteOrderKey, orders_Data_1);
                            }
                            //Log.Information("Added Order Details in Dict");
                        }
                        else
                        {
                            /*Console.WriteLine(orders_Data_1.Instrument.Name.ToString());
                            Console.WriteLine(orders_Data_1.BuySell.ToString());
                            Console.WriteLine(orders_Data_1.LimitPrice.ToString());
                            Console.WriteLine(orders_Data_1.OrderQuantity.ToString());
                            Console.WriteLine(orders_Data_1.WorkingQuantity.ToString());
                            Console.WriteLine(orders_Data_1.SiteOrderKey.ToString());*/
                            MessageBox.Show(string.Format("Data Invalid for: " + orders_Data_1.Instrument.Name.ToString() + " " + orders_Data_1.BuySell.ToString() + " " + orders_Data_1.LimitPrice.ToString()
                                 + " " + orders_Data_1.OrderQuantity.ToString() + " " + orders_Data_1.WorkingQuantity.ToString() + " " + orders_Data_1.SiteOrderKey.ToString()));
                        }
                    }
                }

                richTextBox1.AppendText("\nFile Read Complete for QS!");
                button1.Show();
                button3.Show();
                button4.Show();
                richTextBox2.Show();
                checkBox1.Show();
                checkBox2.Show();
                checkBox3.Show();
            }
        }
        private void Read_File_ASE(string dict_name, ConcurrentDictionary<string, tt_net_sdk.Instrument> instr_dict)
        {
            // Reading File:
            var config = new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture);
            using (var stream = File.Open(CSV_File_Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            using (var csv = new CsvReader(reader, config))
            {
                string[] database = csv.HeaderRecord;

                var dataBase = csv.GetRecords<Orders_Data>();
                Log.Information("Reading CSV File for ASE");
                richTextBox1.AppendText("\nReading CSV File for ASE...");
                foreach (var data in dataBase)
                {
                    if (data.ProdType == "Synthetic")
                    {
                        //Log.Information("Adding ASE Orders to Dict");
                        Orders_Data_1 orders_Data_1 = new Orders_Data_1();

                        // Getting Instrument:
                        if (dict_name == "ASE")
                        {
                            foreach (var instrData in instr_dict)
                            {
                                if (instrData.Key == data.Alias)
                                {
                                    this.instr = instrData.Value;
                                    Log.Information("Found ASE_OR Instrument: " + this.instr.Name);
                                    orders_Data_1.Instrument = this.instr;
                                }
                            }
                        }

                        // Checking Buysell:
                        if (data.BuySell == "Buy")
                        {
                            orders_Data_1.BuySell = BuySell.Buy;
                        }
                        else
                        {
                            orders_Data_1.BuySell = BuySell.Sell;
                        }

                        // Checking OrderType:
                        if (data.OrderType == "Limit")
                        {
                            orders_Data_1.OrderType = OrderType.Limit;
                        }
                        else if (data.OrderType == "Market")
                        {
                            orders_Data_1.OrderType = OrderType.Market;
                        }
                        else if (data.OrderType == "StopLimit")
                        {
                            orders_Data_1.OrderType = OrderType.StopLimit;
                        }
                        else if (data.OrderType == "Stop")
                        {
                            orders_Data_1.OrderType = OrderType.Stop;
                        }
                        else if (data.OrderType == "StopMarketToLimit")
                        {
                            orders_Data_1.OrderType = OrderType.StopMarketToLimit;
                        }
                        else if (data.OrderType == "MarketCloseToday")
                        {
                            orders_Data_1.OrderType = OrderType.MarketCloseToday;
                        }
                        else if (data.OrderType == "NotSet")
                        {
                            orders_Data_1.OrderType = OrderType.NotSet;
                        }
                        else
                        {
                            orders_Data_1.OrderType = OrderType.NotSet;
                        }

                        // Adding Price:
                        orders_Data_1.LimitPrice = Price.FromDecimal(instr, data.LimitPrice);

                        // Adding OrderQuamtity:
                        orders_Data_1.OrderQuantity = Quantity.FromDecimal(instr, data.OrderQuantity);

                        // Adding FillQuamtity:
                        orders_Data_1.FilledQuantity = Quantity.FromDecimal(instr, data.FilledQuantity);

                        // Adding WorkingQuamtity:
                        orders_Data_1.WorkingQuantity = Quantity.FromDecimal(instr, data.WorkingQuantity);

                        // Checking TIF:
                        if (data.TimeInForce == tt_net_sdk.TimeInForce.GoodTillCancel.ToString())
                        {
                            orders_Data_1.TimeInForce = tt_net_sdk.TimeInForce.GoodTillCancel;
                        }
                        else if (data.TimeInForce == tt_net_sdk.TimeInForce.Day.ToString())
                        {
                            orders_Data_1.TimeInForce = tt_net_sdk.TimeInForce.Day;
                        }
                        else if (data.TimeInForce == tt_net_sdk.TimeInForce.FillOrKill.ToString())
                        {
                            orders_Data_1.TimeInForce = tt_net_sdk.TimeInForce.FillOrKill;
                        }
                        else
                        {
                            orders_Data_1.TimeInForce = tt_net_sdk.TimeInForce.GoodTillCancel;
                        }

                        // Adding TextA:
                        orders_Data_1.TextA = data.TextA;

                        // Adding SiteOrderKey:
                        orders_Data_1.SiteOrderKey = data.SiteOrderKey;

                      
                        if (orders_Data_1.LimitPrice.IsValid & orders_Data_1.OrderQuantity.IsValid & orders_Data_1.WorkingQuantity.IsValid & orders_Data_1.FilledQuantity.IsValid)
                        {
                            if (!ase_orders_dict.ContainsKey(data.SiteOrderKey))
                            {
                                ase_orders_dict.TryAdd(data.SiteOrderKey, orders_Data_1);
                            }
                            //Log.Information("Added Order Details in Dict");
                        }
                        else
                        {
                           
                            MessageBox.Show(string.Format("Data Invalid for: " + orders_Data_1.Instrument.Name.ToString() + " " + orders_Data_1.BuySell.ToString() + " " + orders_Data_1.LimitPrice.ToString()
                                 + " " + orders_Data_1.OrderQuantity.ToString() + " " + orders_Data_1.WorkingQuantity.ToString() + " " + orders_Data_1.SiteOrderKey.ToString()));
                        }
                    }
                }

                richTextBox1.AppendText("\nFile Read Complete for ASE!");
                button1.Show();
                button3.Show();
                button4.Show();
                richTextBox2.Show();
                checkBox1.Show();
                checkBox2.Show();
                checkBox3.Show();
            }
        }


        public OrderProfile Send_Order(tt_net_sdk.Instrument instrument, BuySell buysell, Price price, Quantity quantity, OrderType orderType, tt_net_sdk.TimeInForce TIF,
            IReadOnlyCollection<Account> m_account, int acc_idx, string text)
        {
            OrderProfile op = new OrderProfile(instrument)
            {
                BuySell = buysell,
                OrderType = orderType,
                TimeInForce = TIF,
                Account = m_account.ElementAt(acc_idx),
                LimitPrice = price,
                OrderQuantity = quantity,
                TextA = text
            };
            /*op.BuySell = buysell;
            op.OrderType = orderType;
            op.TimeInForce = TIF;
            op.Account = m_account.ElementAt(acc_idx);
            op.LimitPrice = price;
            op.OrderQuantity = quantity;
            op.TextA = text;*/

            return op;
        }
        public void Send_Order_Normal(Dictionary<string, Orders_Data_1> dict, string dict_name, TradeSubscription trade_subscription, IReadOnlyCollection<Account> m_account, int acc_idx)
        {
            foreach (string order_key in dict.Keys)
            {
                tt_net_sdk.Instrument instr = dict[order_key].Instrument;
                BuySell buysell = dict[order_key].BuySell;
                OrderType orderType = dict[order_key].OrderType;
                Price price = dict[order_key].LimitPrice;
                Quantity working_quantity = dict[order_key].WorkingQuantity;
                tt_net_sdk.TimeInForce TIF = dict[order_key].TimeInForce;
                string text = dict[order_key].TextA;

                OrderProfile op = Send_Order(instr, buysell, price, working_quantity, orderType, TIF, m_account, acc_idx, text);
                if (!trade_subscription.SendOrder(op))
                {
                    Log.Information("Error while Adding " + dict_name + " orders " + op.SiteOrderKey);
                    //form2.richTextBox1.AppendText("\nError while Adding " + dict_name + " orders " + op.SiteOrderKey);
                }
                else
                {
                    Log.Information(dict_name + " order Added " + op.SiteOrderKey);
                    //form2.richTextBox1.AppendText("\n" + dict_name + " order Added " + op.SiteOrderKey);
                }
            }
        }
        public void Send_Orders_Task_Creator(ConcurrentDictionary<string, Orders_Data_1> dict, string dict_name, TradeSubscription trade_subscription, IReadOnlyCollection<Account> m_account,  PriceUpdateService updateService)
        {
            Task[] taskArray = new Task[dict.Count];
            int i = 0;
            foreach (string order_key in dict.Keys)
            {
                
                sub2 = new PriceSubscriber(dict[order_key].Instrument);
                updateService.Subscribe(dict[order_key].Instrument, sub2);
                bool b;
                if (dict[order_key].BuySell == BuySell.Buy)
                {
                    b = true;
                }
                else
                {
                    b = false;
                }
                bool market_check = false;
                if (dict[order_key].Instrument.Key.MarketId == MarketId.CME)
                {

                    market_check = HandlePriceUpdate3(sub2, b, dict[order_key].LimitPrice, dict, order_key, dict_name, trade_subscription, m_account, account_idx_cme, taskArray, i, updateService);
                   


                }
                else if (dict[order_key].Instrument.Key.MarketId == MarketId.ICEL)
                {
                   
                        market_check = HandlePriceUpdate3(sub2, b, dict[order_key].LimitPrice, dict, order_key, dict_name, trade_subscription, m_account, account_idx_ice, taskArray, i, updateService);
                   
                  
                   

                }
                else if (dict[order_key].Instrument.Key.MarketId == MarketId.ASE)
                {
                    if (dict[order_key].Instrument.GetSpreadDetails().GetLeg(0).Instrument.Key.MarketId == MarketId.CME)
                    {
                        
                            market_check = HandlePriceUpdate3(sub2, b, dict[order_key].LimitPrice, dict, order_key, dict_name, trade_subscription, m_account, account_idx_cme, taskArray, i, updateService);
                        
                    }
                    else if (dict[order_key].Instrument.GetSpreadDetails().GetLeg(0).Instrument.Key.MarketId == MarketId.ICEL)
                    {
                       
                            market_check = HandlePriceUpdate3(sub2, b, dict[order_key].LimitPrice, dict, order_key, dict_name, trade_subscription, m_account, account_idx_ice, taskArray, i, updateService);
                           
                    }
                }

                i++;
            }
        }

        public void Send_Order_Reload_Check(ConcurrentDictionary<string, Orders_Data_1> dict, string order_key, string dict_name,
            TradeSubscription trade_subscription, IReadOnlyCollection<Account> m_account, int acc_idx)
        {
            tt_net_sdk.Instrument instr = dict[order_key].Instrument;
            BuySell buysell = dict[order_key].BuySell;
            OrderType orderType = dict[order_key].OrderType;
            Price price = dict[order_key].LimitPrice;
            Quantity order_quantity = dict[order_key].OrderQuantity;
            Quantity working_quantity = dict[order_key].WorkingQuantity;
            Quantity fill_quantity = dict[order_key].FilledQuantity;
            tt_net_sdk.TimeInForce TIF = dict[order_key].TimeInForce;
            string text = dict[order_key].TextA;

            // Checking for reload:
            bool is_reload = false;
            if (order_quantity - fill_quantity != working_quantity)
            {
                is_reload = true;
            }
            else
            {
                is_reload = false;
            }
           
            if (is_reload)
            {
                Quantity quantity = order_quantity - fill_quantity;
                
                OrderProfile op = Send_Order(instr, buysell, price, quantity, orderType, TIF, m_account, acc_idx, text);
                op.DisclosedQuantity = working_quantity;
                if (!trade_subscription.SendOrder(op))
                {
                    Log.Information("Error while Adding " + dict_name + " orders " + op.SiteOrderKey);
                    
                }
                else
                {
                    Log.Information(dict_name + " order Added " + op.SiteOrderKey);
                   
                }
            }
            else
            {
                Quantity quantity = order_quantity - fill_quantity;

                OrderProfile op = Send_Order(instr, buysell, price, quantity, orderType, TIF, m_account, acc_idx, text);
                if (!trade_subscription.SendOrder(op))
                {
                    Log.Information("Error while Adding " + dict_name + " orders " + op.SiteOrderKey);

                }
                else
                {
                    Log.Information(dict_name + " order Added " + op.SiteOrderKey);

                }
            }
        }

        private void Lookup_Completed_Handler(string Lookup_Category_Name, ConcurrentDictionary<string, tt_net_sdk.Instrument> instr_dict)
        {
            if (Lookup_Category_Name == "OR")
            {
                or_lookup_done = true;

                this.or_instruments = instr_dict;
            }

            if (Lookup_Category_Name == "QS")
            {
                qs_lookup_done = true;

                this.qs_instruments = instr_dict;
            }

            if (Lookup_Category_Name == "ASE")
            {
                ase_lookup_done = true;

                this.ase_instruments = instr_dict;
            }

            if(or_lookup_done & qs_lookup_done & ase_lookup_done & !trade_sub_started)
            {
                trade_sub_started = true;
                Start_TradeSubscription();
            }
        }

        void m_instrumentTradeSubscription_OrderBookDownload(object sender, OrderBookDownloadEventArgs e)
        {
            Log.Information("Orderbook downloaded...");
            m_isOrderBookDownloaded = true;

            panel1.Show();
        }
        void m_instrumentTradeSubscription_OrderRejected(object sender, OrderRejectedEventArgs e)
        {
            Log.Information("OrderRejected [{0}] : {1} - {2}", e.Order.SiteOrderKey, e.Message, e.OrderRejectReason.ToString());
        }
        void m_instrumentTradeSubscription_OrderFilled(object sender, OrderFilledEventArgs e)
        {
            if (e.FillType == tt_net_sdk.FillType.Full)
            {
                Log.Information("OrderFullyFilled [{0}]: {1}@{2}", e.Fill.SiteOrderKey, e.Fill.Quantity, e.Fill.MatchPrice);
            }
            else
            {
                Log.Information("OrderPartiallyFilled [{0}]: {1}@{2}", e.Fill.SiteOrderKey, e.Fill.Quantity, e.Fill.MatchPrice);
            }
        }
        void m_instrumentTradeSubscription_OrderDeleted(object sender, OrderDeletedEventArgs e)
        {
            Log.Information("OrderDeleted [{0}]", e.OldOrder.SiteOrderKey);
        }
        void m_instrumentTradeSubscription_OrderAdded(object sender, OrderAddedEventArgs e)
        {
            Log.Information("OrderAdded [{0}] {1}: {2}", e.Order.SiteOrderKey, e.Order.BuySell, e.Order.ToString());
        }
        void m_instrumentTradeSubscription_OrderUpdated(object sender, OrderUpdatedEventArgs e)
        {
            Log.Information("OrderUpdated [{0}] with price={1}", e.OldOrder.SiteOrderKey, e.OldOrder.LimitPrice);
        }

        public void TTAPI_ShutdownCompleted(object sender, EventArgs e)
        {
            // Dispose of any other objects / resources
            Log.Information("TTAPI shutdown completed");
        }
        public void Dispose()
        {
            lock (m_Lock)
            {
                if (!m_isDisposed)
                {
                    // Unattached callbacks and dispose of all subscriptions
                    if (m_instrumentTradeSubscription != null)
                    {
                        m_instrumentTradeSubscription.OrderUpdated -= m_instrumentTradeSubscription_OrderUpdated;
                        m_instrumentTradeSubscription.OrderAdded -= m_instrumentTradeSubscription_OrderAdded;
                        m_instrumentTradeSubscription.OrderDeleted -= m_instrumentTradeSubscription_OrderDeleted;
                        m_instrumentTradeSubscription.OrderFilled -= m_instrumentTradeSubscription_OrderFilled;
                        m_instrumentTradeSubscription.OrderRejected -= m_instrumentTradeSubscription_OrderRejected;
                        m_instrumentTradeSubscription.Dispose();
                        m_instrumentTradeSubscription = null;
                    }
                    m_isDisposed = true;
                    Log.Information("Disposed");
                }
                TTAPI.Shutdown();
            }
        }
    }

    public class Orders_Data
    {
        [Name(name: "TimeStamp")]
        public DateTime TimeStamp { get; set; }

        [Name(name: "MarketID")]
        public string MarketID { get; set; }

        [Name(name: "ProdType")]
        public string ProdType { get; set; }

        [Name(name: "ProdName")]
        public string ProdName { get; set; }

        [Name(name: "Alias")]
        public string Alias { get; set; }

        [Name(name: "BuySell")]
        public string BuySell { get; set; }

        [Name(name: "OrderType")]
        public string OrderType { get; set; }

        [Name(name: "LimitPrice")]
        public decimal LimitPrice { get; set; }

        [Name(name: "OrderQuantity")]
        public decimal OrderQuantity { get; set; }

        [Name(name: "FilledQuantity")]
        public decimal FilledQuantity { get; set; }

        [Name(name: "WorkingQuantity")]
        public decimal WorkingQuantity { get; set; }

        [Name(name: "TimeInForce")]
        public string TimeInForce { get; set; }

        [Name(name: "TextA")]
        public string TextA { get; set; }

        [Name(name: "SiteOrderKey")]
        public string SiteOrderKey { get; set; }
    }
    public class Order_Data_Map : ClassMap<Orders_Data>
    {
        public Order_Data_Map()
        {
            Map(m => m.TimeStamp).Index(0).Name("TimeStamp");
            Map(m => m.MarketID).Index(1).Name("MarketID");
            Map(m => m.ProdType).Index(2).Name("ProdType");
            Map(m => m.ProdName).Index(3).Name("ProdName");
            Map(m => m.Alias).Index(4).Name("Alias");
            Map(m => m.BuySell).Index(5).Name("BuySell");
            Map(m => m.OrderType).Index(6).Name("OrderType");
            Map(m => m.LimitPrice).Index(7).Name("LimitPrice");
            Map(m => m.OrderQuantity).Index(8).Name("OrderQuantity");
            Map(m => m.FilledQuantity).Index(9).Name("FilledQuantity");
            Map(m => m.WorkingQuantity).Index(10).Name("WorkingQuantity");
            Map(m => m.TimeInForce).Index(11).Name("TimeInForce");
            Map(m => m.TextA).Index(12).Name("TextA");
            Map(m => m.SiteOrderKey).Index(13).Name("SiteOrderKey");
        }
    }

    public class Orders_Data_1
    {
        [Name(name: "TimeStamp")]
        public DateTime TimeStamp { get; set; }

        [Name(name: "Instrument")]
        public tt_net_sdk.Instrument Instrument { get; set; }

        [Name(name: "BuySell")]
        public BuySell BuySell { get; set; }

        [Name(name: "OrderType")]
        public OrderType OrderType { get; set; }

        [Name(name: "LimitPrice")]
        public Price LimitPrice { get; set; }

        [Name(name: "OrderQuantity")]
        public Quantity OrderQuantity { get; set; }

        [Name(name: "FilledQuantity")]
        public Quantity FilledQuantity { get; set; }

        [Name(name: "WorkingQuantity")]
        public Quantity WorkingQuantity { get; set; }

        [Name(name: "TimeInForce")]
        public tt_net_sdk.TimeInForce TimeInForce { get; set; }

        [Name(name: "TextA")]
        public string TextA { get; set; }

        [Name(name: "SiteOrderKey")]
        public string SiteOrderKey { get; set; }
    }
    public class Order_Data_Map_1 : ClassMap<Orders_Data_1>
    {
        public Order_Data_Map_1()
        {
            Map(m => m.TimeStamp).Index(0).Name("TimeStamp");
            Map(m => m.Instrument).Index(1).Name("Instrument");
            Map(m => m.BuySell).Index(5).Name("BuySell");
            Map(m => m.OrderType).Index(6).Name("OrderType");
            Map(m => m.LimitPrice).Index(7).Name("LimitPrice");
            Map(m => m.OrderQuantity).Index(8).Name("OrderQuantity");
            Map(m => m.FilledQuantity).Index(9).Name("FilledQuantity");
            Map(m => m.WorkingQuantity).Index(10).Name("WorkingQuantity");
            Map(m => m.TimeInForce).Index(11).Name("TimeInForce");
            Map(m => m.TextA).Index(12).Name("TextA");
            Map(m => m.SiteOrderKey).Index(13).Name("SiteOrderKey");
        }
    }
}
