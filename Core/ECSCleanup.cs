namespace SETUE.Core
{
    public static class ECSCleanup
    {
        public static void Execute()
        {
            Object.ECSWorld.ExecuteCommands();
        }
    }
}
