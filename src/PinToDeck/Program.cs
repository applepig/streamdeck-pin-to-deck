using BarRaider.SdTools;

namespace PinToDeck
{
    class Program
    {
        static void Main(string[] args)
        {
            // Uncomment this line to enable logging
            //SDWrapper.EnableLogging = true;

            // Stream Deck will launch this entry point and manage the plugin lifecycle
            SDWrapper.Run(args);
        }
    }
}
