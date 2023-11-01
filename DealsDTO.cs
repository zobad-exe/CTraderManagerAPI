using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CTraderManagerAPI
{
    public class DealsDTO
    {
        public DealsDTO()
        {
                
        }

        public DealsDTO(string platform, int dealId, int positionId, int login, int traderId, string action, string entry, string time, string symbol, int volume, string rateProfit, string rateMargin, string comment, double profit, int volumeClosed, double contractSize, double price, double priceSL, double priceTP)
        {
            this.platform = platform;
            this.dealId = dealId;
            this.positionId = positionId;
            this.login = login;
            this.traderId = traderId;
            this.action = action;
            this.entry = entry;
            this.time = time;
            this.symbol = symbol;
            this.volume = volume;
            this.rateProfit = rateProfit;
            this.rateMargin = rateMargin;
            this.comment = comment;
            this.profit = profit;
            this.volumeClosed = volumeClosed;
            this.contractSize = contractSize;
            this.price = price;
            this.priceSL = priceSL;
            this.priceTP = priceTP;
        }

        public string platform { get;set; }
        public int dealId { get;set; }
        public int positionId { get;set; }
        public int login { get;set; }
        public int traderId { get;set; }
        public string action { get;set; }//buy,sell
        public string entry { get;set; } //In,Out
        public string time { get;set; }
        public string symbol { get;set; }
        public int volume { get;set; }
        public string rateProfit { get;set; }
        public string rateMargin { get;set; }
        public string comment { get;set; }
        public double profit { get;set; }
        public long volumeClosed { get;set; }
        public double contractSize { get;set; }
        public double price { get;set; }
        public double priceSL { get;set; }
        public double priceTP { get;set; }
    }
}
