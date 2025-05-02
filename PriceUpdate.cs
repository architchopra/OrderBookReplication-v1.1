using System;
using System.Collections.Generic;

using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tt_net_sdk;

namespace Order_Book_Replication

{
    public struct PriceUpdate
    {
        public Instrument Symbol { get; }
        public Price BidPrice { get; }
        public Price AskPrice { get; }
        public Quantity AskQuantity { get; }
        public Quantity BidQuantity { get; }

        public PriceUpdate(Instrument symbol, Price BidPrice, Price AskPrice, Quantity BidQuantity, Quantity AskQuantity)
        {
            Symbol = symbol;
            this.BidPrice = BidPrice;
            this.AskPrice = AskPrice;
            this.AskQuantity = AskQuantity;
            this.BidQuantity = BidQuantity;
        }
    }

}
