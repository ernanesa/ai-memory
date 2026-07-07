namespace AiMemory.Services
{
    public static class TrayLaunchService
    {
        public static bool TryStart(out string? errorMessage)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ai-memory-tray",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                System.Diagnostics.Process.Start(psi);
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}
