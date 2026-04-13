using SETUE.Core;

namespace SETUE
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Engine Starting ===");
            
            MasterClock.Load();
            
            // This is now blocking and will run the entire engine on this thread
            MasterClock.Start();
            
            Console.WriteLine("=== Engine Shutdown ===");
        }
    }
}
