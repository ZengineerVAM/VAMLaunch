using System;

namespace VAMLaunch
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine(@"
** VAM LAUNCH SERVER ***************************************************************************
");
            VAMLaunchServer server = new VAMLaunchServer();
            server.Run();
        }
    }
}