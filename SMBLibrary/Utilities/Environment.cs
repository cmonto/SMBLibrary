#if NETSTANDARD1_3
namespace Utilities
{
    public static class Environment
    {
        public static string MachineName
        {
            get
            {
                return System.Environment.GetEnvironmentVariable("CUMPUTERNAME") ?? System.Environment.GetEnvironmentVariable("HOSTNAME") ?? "DEVICE";
            }
        }
    }
}
#endif
