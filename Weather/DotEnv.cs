namespace Weather;

public static class DotEnv
{
    public static void Load(string fileName = ".env")
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, fileName);

            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var separator = line.IndexOf('=');
                if (separator <= 0) continue;
                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim().Trim('"', '\'');
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                    Environment.SetEnvironmentVariable(key, value);
            }

            return;
        }
    }
}
