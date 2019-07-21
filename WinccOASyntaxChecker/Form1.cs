using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WccSyntaxCheck
{
    public partial class Form1 : Form
    {
        private string _filename;

        public Form1()
        {
            InitializeComponent();

            try
            {
                DoWork();
            }
            catch (Exception e)
            {
                textBox1.Text = @"ОШИБКА: " + e.Message;
            }
        }

        private void DoWork()
        {
            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();

            var filename = args.Any() ? args[0] : string.Empty;
            if (string.IsNullOrEmpty(filename) || !File.Exists(filename))
                throw new Exception("Файл не найден: " + filename);

            if (!filename.ToLower().EndsWith(".ctl"))
                throw new Exception("Файл не является скриптом");

            _filename = filename;
            CheckSyntax(filename);
            SubscribeOnChange();
        }

        private void SubscribeOnChange()
        {
            var watcher = new FileSystemWatcher
            {
                Path = Path.GetDirectoryName(_filename),
                NotifyFilter = NotifyFilters.LastWrite,
                Filter = Path.GetFileName(_filename),
                EnableRaisingEvents = true
            };
            watcher.Changed += OnChanged;
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            CheckSyntax(_filename);
        }

        private void CheckSyntax(string filename)
        {
            var executeFileName = GetExecuteFileName(filename); 
            var projectName = GetProjectName(filename);
            var relativeFileName = GetRelativeFileName(filename);
            
            var parameters = new[]
            {
                "-n",
                "-proj " + projectName,
                relativeFileName,
                "-syntax",
                "-log +stderr"
            };

            var cmd = new Process
            {
                StartInfo =
                {
                    FileName = executeFileName,
                    Arguments = string.Join(" ", parameters),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            var error = new List<string>();

            using (var errorWaitHandle = new AutoResetEvent(false))
            {
                cmd.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        errorWaitHandle.Set();
                    }
                    else
                    {
                        error.Add(Encoding.UTF8.GetString(Encoding.GetEncoding(1251).GetBytes(e.Data)));
                    }
                };

                cmd.Start();

                const int timeout = 100000;

                cmd.BeginErrorReadLine();

                if (cmd.WaitForExit(timeout) &&
                    errorWaitHandle.WaitOne(timeout))
                {
                    var text = ParseOutput(error);

                    if (textBox1.InvokeRequired)
                    {
                        textBox1.Invoke((MethodInvoker) delegate()
                        {
                            textBox1.Text = text;
                        });
                    }
                    else
                    {
                        textBox1.Text = text;
                    }
                }
            }
        }

        private string GetExecuteFileName(string filename)
        {
            var configFileName = GetConfigFileName(filename);

            var pvssPath = File.ReadAllLines(configFileName)
                .FirstOrDefault(l => l.Trim().StartsWith("pvss_path"));

            if (pvssPath == null)
                throw new Exception("Не найден путь к WinCC OA");

            var path = pvssPath.Split('=').Skip(1).First().Trim(' ', '"').Replace('/', '\\');
            if (!Directory.Exists(path))
                throw new Exception("Не найдена папка WinCC OA: " + path);

            Text += " (" + path + ")";

            return Path.Combine(path, "bin\\WCCOActrl.exe");
        }

        private string ParseOutput(IEnumerable<string> output)
        {
            var lines = output
                .Select(l => l.Split(',').Select(p => p.Trim()).ToArray())
                .Where(l => l[2].ToLower() != "sys")
                .Select(l => string.Join(", ", l.Skip(5).ToArray()))
                .ToArray();

            return lines.Any() ? string.Join("\r\n", lines) : "<No errors>";
        }

        private string GetRelativeFileName(string filename)
        {
            var fi = new DirectoryInfo(filename);
            var result = new List<string>();

            while (true)
            {
                if (Directory.Exists(Path.Combine(fi.FullName, "config")) &&
                    Directory.Exists(Path.Combine(fi.FullName, "scripts")))
                {
                    return string.Join("\\", result.Skip(1).ToArray());
                }

                result.Insert(0, fi.Name);
                fi = fi.Parent;
                if (fi == null)
                    throw new Exception("Папка проекта не найдена");
            }
        }

        private string GetProjectName(string filename)
        {
            var fi = new DirectoryInfo(filename);

            while (true)
            {
                if (Directory.Exists(Path.Combine(fi.FullName, "config")) &&
                    Directory.Exists(Path.Combine(fi.FullName, "scripts")))
                {
                    return fi.Name;
                }

                fi = fi.Parent;
                if (fi == null)
                    throw new Exception("Папка проекта не найдена");
            }
        }

        private string GetConfigFileName(string filename)
        {
            var fi = new DirectoryInfo(filename);

            while (true)
            {
                if (Directory.Exists(Path.Combine(fi.FullName, "config")) &&
                    Directory.Exists(Path.Combine(fi.FullName, "scripts")))
                {
                    return Path.Combine(fi.FullName, "config\\config");
                }

                fi = fi.Parent;
                if (fi == null)
                    throw new Exception("Папка проекта не найдена");
            }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
                Close();
        }
    }
}
