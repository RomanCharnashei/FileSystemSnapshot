using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace FileSystemSnapshot
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== Optimized file system snapshot creation ===");

            // Default values
            string defaultSource = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string defaultOutput = Path.Combine("e:\\", "files_snapshot.csv");

            // Request paths with editable prompts
            Console.WriteLine("\nEnter the path to the scanned folder (by default: {0}):", defaultSource);
            string sourcePath = ReadLineWithDefault(defaultSource);

            Console.WriteLine("\nEnter path to save CSV (by default: {0}):", defaultOutput);
            string outputFile = ReadLineWithDefault(defaultOutput);


            try
            {
                // Create and save snapshot
                int processedCount = await CreateAndSaveSnapshotAsync(sourcePath, outputFile);

                Console.WriteLine($"\nSuccessful! Processed files: {processedCount}");
                Console.WriteLine($"File is saved: {Path.GetFullPath(outputFile)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nFatal error: {ex.Message}");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        static async Task<int> CreateAndSaveSnapshotAsync(string rootPath, string outputPath)
        {
            if (!Directory.Exists(rootPath))
                throw new DirectoryNotFoundException($"Folder not found: {rootPath}");

            int processedCount = 0;
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                ReturnSpecialDirectories = false
            };

            using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
            {
                // CSV Header
                await writer.WriteLineAsync("FullPath,Hash");

                // Asynchronous file processing
                await foreach (var file in EnumerateFilesWithProgressAsync(rootPath, options))
                {
                    try
                    {
                        string hash = ComputeFileHash(file);
                        await writer.WriteLineAsync($"\"{EscapeCsv(file)}\",{hash}");
                        processedCount++;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Console.WriteLine($"No access to file: {file}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Processing error {file}: {ex.Message}");
                    }
                }
            }

            return processedCount;
        }

        static async IAsyncEnumerable<string> EnumerateFilesWithProgressAsync(
            string rootPath,
            EnumerationOptions options)
        {
            var lastUpdate = DateTime.MinValue;
            int counter = 0;

            await foreach (string file in SafeEnumerateFilesAsync(rootPath, "*", options))
            {
                yield return file;
                counter++;

                // Update progress every 100 files or 0.5 seconds
                if (counter % 100 == 0 || (DateTime.Now - lastUpdate).TotalSeconds > 0.5)
                {
                    Console.Write($"\rFiles Found: {counter}");
                    lastUpdate = DateTime.Now;
                }
            }

            Console.WriteLine($"\rAll files: {counter}".PadRight(Console.WindowWidth));
        }

        static async IAsyncEnumerable<string> SafeEnumerateFilesAsync(
            string root,
            string pattern,
            EnumerationOptions options)
        {
            Stack<string> dirs = new Stack<string>();
            dirs.Push(root);

            while (dirs.Count > 0)
            {
                string currentDir = dirs.Pop();
                string[]? subDirs = null;
                string[]? files = null;

                // Receive files
                try
                {
                    files = Directory.GetFiles(currentDir, pattern);
                }
                catch (UnauthorizedAccessException)
                {
                    Console.WriteLine($"No access to folder: {currentDir}");
                    files = Array.Empty<string>();
                }
                catch (DirectoryNotFoundException)
                {
                    files = Array.Empty<string>();
                }

                // Processing files
                foreach (string file in files)
                {
                    yield return file;
                }

                // Get subdirectory
                try
                {
                    subDirs = Directory.GetDirectories(currentDir);
                }
                catch (UnauthorizedAccessException)
                {
                    Console.WriteLine($"No access to subfolders: {currentDir}");
                    subDirs = Array.Empty<string>();
                }
                catch (DirectoryNotFoundException)
                {
                    subDirs = Array.Empty<string>();
                }

                // Add subdirectory to stack
                foreach (string subDir in subDirs)
                {
                    dirs.Push(subDir);
                }

                // Return control for asynchrony
                await Task.Yield();
            }
        }

        static string ComputeFileHash(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite
            );

            byte[] hashBytes = sha256.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\"", "\"\"");
        }

        static string ReadLineWithDefault(string defaultValue)
        {
            // Set the initial cursor position
            int left = Console.CursorLeft;
            int top = Console.CursorTop;

            // Output the default value as if it was already entered
            Console.Write(defaultValue);

            // Create an input buffer
            var inputBuffer = new StringBuilder(defaultValue);
            int position = defaultValue.Length;

            ConsoleKeyInfo key;
            do
            {
                // Position cursor
                Console.SetCursorPosition(left + position, top);

                key = Console.ReadKey(true);

                switch (key.Key)
                {
                    case ConsoleKey.LeftArrow:
                        if (position > 0) position--;
                        break;

                    case ConsoleKey.RightArrow:
                        if (position < inputBuffer.Length) position++;
                        break;

                    case ConsoleKey.Backspace:
                        if (position > 0)
                        {
                            inputBuffer.Remove(position - 1, 1);
                            position--;

                            // Rewrite the line
                            RedrawLine(left, top, inputBuffer, position);
                        }
                        break;

                    case ConsoleKey.Delete:
                        if (position < inputBuffer.Length)
                        {
                            inputBuffer.Remove(position, 1);
                            RedrawLine(left, top, inputBuffer, position);
                        }
                        break;

                    case ConsoleKey.Escape:
                        // Clear input
                        inputBuffer.Clear();
                        position = 0;
                        RedrawLine(left, top, inputBuffer, position);
                        break;

                    case ConsoleKey.Home:
                        position = 0;
                        break;

                    case ConsoleKey.End:
                        position = inputBuffer.Length;
                        break;

                    default:
                        // Common character processing
                        if (!char.IsControl(key.KeyChar))
                        {
                            inputBuffer.Insert(position, key.KeyChar);
                            position++;
                            RedrawLine(left, top, inputBuffer, position);
                        }
                        break;
                }
            }
            while (key.Key != ConsoleKey.Enter);

            Console.WriteLine(); // Jump to new string after Enter

            return inputBuffer.ToString();
        }

        static void RedrawLine(int left, int top, StringBuilder input, int position)
        {
            // Remember current cursor position
            int currentLeft = Console.CursorLeft;
            int currentTop = Console.CursorTop;

            // Rewrite the line
            Console.SetCursorPosition(left, top);
            Console.Write(input.ToString() + new string(' ', Console.WindowWidth - input.Length - left - 1));

            // Returns the cursor to the correct position
            Console.SetCursorPosition(left + position, top);

            // Resetting the cursor position if needed
            Console.SetCursorPosition(currentLeft, currentTop);
        }
    }
}