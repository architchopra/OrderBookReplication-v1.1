using Order_Book_Replication;
using System;
using System.Windows.Forms;
using tt_net_sdk;

namespace WindowsFormsApp1
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            /*
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
            */

            using (Dispatcher disp = Dispatcher.AttachUIDispatcher())
            {
                Application.EnableVisualStyles();
                // Create an instance of the API
                Form1 myApp = new Form1();
                
                string appSecretKey = "55317511-0d8b-0561-1227-17f27d74ca34:b95df911-04f5-1ec0-4ed6-47f225753bcd"; //Size - 25
                
                tt_net_sdk.ServiceEnvironment environment = tt_net_sdk.ServiceEnvironment.ProdSim;
                tt_net_sdk.TTAPIOptions apiConfig = new tt_net_sdk.TTAPIOptions(environment, appSecretKey, 5000);
                ApiInitializeHandler handler = new ApiInitializeHandler(myApp.ttNetApiInitHandler);
                TTAPI.CreateTTAPI(disp, apiConfig, handler);
                Application.Run(myApp);
            }
        }
    }
}
