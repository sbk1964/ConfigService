using NUnit.Framework;
using ConfigService;

namespace TestProject1
{

 
    public class Tests
    {
        private ConfigService.ConfigService _configService; // Fix: Use the class inside the namespace

        [SetUp]
        public void Setup()
        {
            _configService = new ConfigService.ConfigService("InputFiles\\manifest.csv", "InputFiles\\cfgs.csv");
        }

        //1.getIntConfig(IOrder order, "CloseAuction","SendTimeOffsetSeconds", 23400)
        //For a VWAP order with aggression as M it'll return 600 (from VWAP)
        //For a VWAP order with aggression as P it'll return 900 (from VWAP_PASSIVE)
        //For a VWAP order with aggression as A it'll return 600 (from VWAP)
        //For a VWAP order with no aggression(null), it'll return 600(from VWAP)
        //For a VWAP order for Account=CLIENTXYZ, it'll return 2000(from VWAP_PASSIVE_XYZ)
        //For a TWAP order, it'll return 60 (only BASE manifest qualifies for this TWAP order)
        //2.getIntConfig(IOrder order, "CloseAuction","CancelSeconds", 23400)
        //For a VWAP order it'll return 23400 as provided default (no row exists for this Cfg)
        //For a TWAP order it'll return 23400 as provided default (no row exists for this Cfg)

        [Test]
        public void Test1()
        {
            Order order = new Order()
            {
                Strategy = "",
                Aggression = "",
                Country = "",
                AssetType = "",
                Account = "",
                TraderId = "Joe"
            };
            int result;
            order.Strategy = "VWAP";
            order.Aggression = "M";
            result = _configService.getIntConfig(order, "CloseAuction", "SendTimeOffsetSeconds", 23400);
            Assert.AreEqual(600, result);

            order.Aggression = "P";
            result = _configService.getIntConfig(order, "CloseAuction", "SendTimeOffsetSeconds", 23400);
            Assert.AreEqual(900, result);

            order.Aggression = "A"; 
            result = _configService.getIntConfig(order, "CloseAuction", "SendTimeOffsetSeconds", 23400);
            Assert.AreEqual(600, result);

            order.Aggression = null;
            result = _configService.getIntConfig(order, "CloseAuction", "SendTimeOffsetSeconds", 23400);
            Assert.AreEqual(600, result);

            order.Aggression = "P";
            order.Account = "CLIENTXYZ";    
            result = _configService.getIntConfig(order, "CloseAuction", "SendTimeOffsetSeconds", 23400);
            Assert.AreEqual(2000, result);

            order.Strategy = "TWAP";
            result = _configService.getIntConfig(order, "CloseAuction", "SendTimeOffsetSeconds", 23400);
            Assert.AreEqual(60, result);

            order.Strategy = "VWAP";
            result = _configService.getIntConfig(order, "CloseAuction", "CancelSeconds", 23400);
            Assert.AreEqual(23400, result);

            order.Strategy = "TWAP";
            result = _configService.getIntConfig(order, "CloseAuction", "CancelSeconds", 23400);
            Assert.AreEqual(23400, result);


        }
    }
}