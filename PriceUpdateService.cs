using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tt_net_sdk;

namespace Order_Book_Replication
{
    public class PriceUpdateService
    {
        private ConcurrentDictionary<Instrument, PricePublisher> publishers = new ConcurrentDictionary<Instrument, PricePublisher>();
        private ConcurrentDictionary<PricePublisher, List<PriceSubscriber>> publisherSubscribersMap = new ConcurrentDictionary<PricePublisher, List<PriceSubscriber>>();
        private Dispatcher m_disp;

        public PriceUpdateService(Dispatcher disp)
        {
            this.m_disp = disp;
        }


        // Method to subscribe to price updates for a symbol
        public void Subscribe(Instrument symbol, PriceSubscriber subx)
        {
            if (!publishers.ContainsKey(symbol))
            {
                Console.WriteLine(symbol);
                Console.WriteLine("checking");
                // Create a publisher for the symbol if it doesn't exist
                PricePublisher publisher = new PricePublisher(symbol, m_disp);
                publishers.TryAdd(symbol, publisher);
                subx.Add(publisher);
                publisherSubscribersMap.TryAdd(publisher, new List<PriceSubscriber>());
                // Start publishing updates for the symbol
                if (publisherSubscribersMap.TryGetValue(publisher, out var subscriberList))
                {
                    lock (subscriberList)
                    {
                        subscriberList.Add(subx);
                    }
                }

                publisher.StartPublishingUpdates();
            }
            else
            {
                Console.WriteLine("checking2");
                if (publisherSubscribersMap.TryGetValue(publishers[symbol], out var subscriberList))
                {
                    lock (subscriberList)
                    {
                        subscriberList.Add(subx);
                    }
                }

                subx.Add(publishers[symbol]);
            }






        }
        public void Unsubscribe(Instrument symbol, PriceSubscriber subx)
        {
            if (publishers.TryGetValue(symbol, out var publisher) && publisherSubscribersMap.TryGetValue(publisher, out var subscriberList))
            {
                Console.WriteLine($"Unsubscribe {symbol}");
                subx.Dispose();
                lock (subscriberList)
                {
                    subscriberList.Remove(subx);

                }
                if (subscriberList.Count == 0)
                {
                    publisher.Dispose();
                    publishers.TryRemove(symbol, out _); // Remove the publisher if no subscribers remain
                }
            }
        }
    }
}
