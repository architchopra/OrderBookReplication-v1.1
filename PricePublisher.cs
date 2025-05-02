using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tt_net_sdk;
using WindowsFormsApp1;

namespace Order_Book_Replication
{
    public class PricePublisher
    {
        public ManualResetEvent priceChangedEvent = new ManualResetEvent(false);
        private readonly Instrument symbol;
        private PriceUpdate latestPriceUpdate;
        private Dispatcher m_disp;
        private PriceSubscription m_priceSubscription = null;
        public PricePublisher(Instrument symbol, Dispatcher disp)
        {
            this.m_disp = disp;
            this.symbol = symbol;
        }

        // Method to start publishing updates
        public void StartPublishingUpdates()
        {
            m_priceSubscription = new PriceSubscription(symbol, m_disp);
            m_priceSubscription.Settings = new PriceSubscriptionSettings(PriceSubscriptionType.MarketDepth);

            m_priceSubscription.FieldsUpdated += PriceSubscription_FieldsUpdated;
            m_priceSubscription.Start();

        }
        public void PriceSubscription_FieldsUpdated(object sender, FieldsUpdatedEventArgs e)
        {
            if (e.Error == null)
            {

                if (e.Fields.GetBestBidPriceField(0).Value != null && e.Fields.GetBestBidPriceField(0).Value.IsValid && e.Fields.GetBestBidPriceField(0).Value.IsTradable && e.Fields.GetBestAskPriceField(0).Value != null && e.Fields.GetBestAskPriceField(0).Value.IsValid && e.Fields.GetBestAskPriceField(0).Value.IsTradable && e.Fields.GetBestBidQuantityField().Value != 0 && e.Fields.GetBestAskQuantityField().Value != 0 && e.Fields.GetBestBidQuantityField().Value.IsValid && e.Fields.GetBestAskQuantityField().Value.IsValid && e.Fields.GetBestBidQuantityField().Value != Quantity.Empty && e.Fields.GetBestAskQuantityField().Value != Quantity.Empty && e.Fields.GetBestBidPriceField(0).Value != Price.Empty && e.Fields.GetBestAskPriceField(0).Value != Price.Empty)
                {

                    latestPriceUpdate = new PriceUpdate(symbol, e.Fields.GetBestBidPriceField(0).Value, e.Fields.GetBestAskPriceField(0).Value, e.Fields.GetBestBidQuantityField().Value, e.Fields.GetBestAskQuantityField().Value);
                    priceChangedEvent.Set();
                }
            }
            else
            {
                if (e.Error != null)
                {
                    Console.WriteLine("Unrecoverable price subscription error: {0}", e.Error.Message);

                }
            }
        }



        // Method to get the latest price update
        public PriceUpdate GetLatestPriceUpdate()
        {
            return latestPriceUpdate;
        }
        public ManualResetEvent GetPriceChangedEvent()
        {
            return priceChangedEvent;
        }
        public void Dispose()
        {

            if (m_priceSubscription != null)
            {
                m_priceSubscription.FieldsUpdated -= PriceSubscription_FieldsUpdated;
                m_priceSubscription.Dispose();
                m_priceSubscription = null;
            }
        }
    }

}

