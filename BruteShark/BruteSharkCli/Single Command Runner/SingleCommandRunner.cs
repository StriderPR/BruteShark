﻿using System;
using System.Collections.Generic;
using System.Text;
using PcapProcessor;
using PcapAnalyzer;
using System.IO;
using System.Linq;
using CommandLine;
using CommonUi;
using System.Reflection;

namespace BruteSharkCli
{
    class SingleCommandRunner
    {
        private SingleCommandFlags _cliFlags;
        private List<string> _files;
        private HashSet<PcapAnalyzer.NetworkFile> _extractedFiles;
        private HashSet<PcapAnalyzer.NetworkPassword> _passwords;
        private HashSet<PcapAnalyzer.NetworkHash> _hashes;
        private HashSet<PcapAnalyzer.NetworkConnection> _connections;
        private HashSet<VoipCallPresentation> _voipCalls;
        private HashSet<PcapAnalyzer.DnsNameMapping> _dnsMappings;
        private PcapProcessor.Processor _processor;
        private PcapAnalyzer.Analyzer _analyzer;

        private readonly Dictionary<string, string> CliModulesNamesToAnalyzerNames = new Dictionary<string, string> {
            { "FileExtracting" , "File Extracting"},
            { "NetworkMap", "Network Map" },
            { "Credentials" ,"Credentials Extractor (Passwords, Hashes)"},
            { "Voip" ,"Voip Calls"}
            { "DNS", "DNS"}

        };

        public SingleCommandRunner(Analyzer analyzer, Processor processor, string[] args)
        { 
            _analyzer = analyzer;
            _processor = processor;
            _files = new List<string>();

            _hashes = new HashSet<PcapAnalyzer.NetworkHash>();
            _connections = new HashSet<PcapAnalyzer.NetworkConnection>();
            _passwords = new HashSet<NetworkPassword>();
            _extractedFiles = new HashSet<NetworkFile>();
            _voipCalls = new HashSet<VoipCallPresentation>();
            _dnsMappings = new HashSet<PcapAnalyzer.DnsNameMapping>();


            _analyzer.ParsedItemDetected += OnParsedItemDetected;
            _analyzer.UpdatedItemProprertyDetected += UpdatedPropertyInItemDetected;

            _processor.ProcessingFinished += (s, e) => this.ExportResults();
            _processor.FileProcessingStatusChanged += (s, e) => this.PrintFileStatusUpdate(s, e);

            // Parse user arguments.
            CommandLine.Parser.Default.ParseArguments<SingleCommandFlags>(args).WithParsed<SingleCommandFlags>((cliFlags) => _cliFlags = cliFlags);
        }

        public void Run()
        {
            try
            {
                SetupRun();
                Console.WriteLine($"[+] Started analyzing {_files.Count} files");
                _processor.ProcessPcaps(_files);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private void PrintFileStatusUpdate(object sender, FileProcessingStatusChangedEventArgs e)
        {
            if (e.Status == FileProcessingStatus.Started || e.Status == FileProcessingStatus.Finished)
            {
                Console.ForegroundColor = ConsoleColor.Green;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }

            Console.WriteLine($"File : {Path.GetFileName(e.FilePath)} Processing {e.Status}");
            Console.ForegroundColor = ConsoleColor.White;
        }

        private void SetupRun()
        {
            // That can happen when the user enter vesion \ help commad, exit gracefully.
            if (_cliFlags is null)
            {
                Environment.Exit(0);
            }

            // Load modules.
            if (_cliFlags?.Modules?.Any() == true)
            {
                LoadModules(ParseCliModuleNames(_cliFlags.Modules));
            }
            else
            {
                throw new Exception("No mudules selected");
            }

            if (_cliFlags.InputFiles.Count() != 0 && _cliFlags.InputDir != null)
            {
                throw new Exception("Only one of the arguments -i and -d can be presented in a single command mode run");
            }
            else if (_cliFlags.InputFiles.Count() != 0)
            {
                foreach (string filePath in _cliFlags.InputFiles)
                {
                    AddFile(filePath);
                }
            }
            else
            {
                VerifyDir(_cliFlags.InputDir);
            }
        }

        private void LoadModules(List<string> modules)
        {
            foreach (string m in modules)
            {
                _analyzer.AddModule(m);
            }
        }

        private List<string> ParseCliModuleNames(IEnumerable<string> modules)
        {
            var analyzerModulesToLoad = new List<string>();

            foreach (var cliModuleName in modules)
            {
                string analyzerModuleName = CliModulesNamesToAnalyzerNames.GetValueOrDefault(cliModuleName, defaultValue: null);

                if (analyzerModuleName != null)
                {
                    analyzerModulesToLoad.Add(analyzerModuleName);
                }
            }

            return analyzerModulesToLoad;
        }

        private void VerifyDir(string dirPath)
        {
            FileAttributes attrs = File.GetAttributes(dirPath);
            if ((attrs & FileAttributes.Directory) == FileAttributes.Directory)
            {
                DirectoryInfo dir = new DirectoryInfo(dirPath);
                foreach (var file in dir.GetFiles("*.*"))
                {
                    AddFile(file.FullName);
                }
            }
            else
            {
                throw new IOException($"{dirPath} is not a valid directory path");
            }
        }

        private void ExportResults()
        {
            if (_cliFlags.OutputDir != null)
            { 
                if (_connections.Any())
                {
                    var filePath = CommonUi.Exporting.ExportNetworkMap(_cliFlags.OutputDir, _connections);
                    Console.WriteLine($"Successfully exported network map to json file: {filePath}");
                }
                if (_hashes.Any())
                {
                    Utilities.ExportHashes(_cliFlags.OutputDir, _hashes);
                }
                if (_files.Any())
                {
                    var dirPath = CommonUi.Exporting.ExportFiles(_cliFlags.OutputDir, _extractedFiles);
                    Console.WriteLine($"Successfully exported extracted files to: {dirPath}");
                }
                if (_dnsMappings.Any())
                {
                    var dnsFilePath = CommonUi.Exporting.ExportDnsMappings(_cliFlags.OutputDir, _dnsMappings);
                    Console.WriteLine($"Successfully exported DNS mappings to file: {dnsFilePath}");
                }
				if(_voipCalls.Any())
                {
                   var dirPath = CommonUi.Exporting.ExportVoipCalls(_cliFlags.OutputDir, _voipCalls);
                   Console.WriteLine($"Successfully exported voip calss extracted to: {dirPath}");
                }
            }

            Console.WriteLine("[X] Bruteshark finished processing");
        }

        private void AddFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                _files.Add(filePath);
            }
            else
            {
                Console.WriteLine($"ERROR: File does not exist - {filePath}");
            }
        }
        private void UpdatedPropertyInItemDetected(object sender, UpdatedPropertyInItemeventArgs e)
        {
            if (e.ParsedItem is PcapAnalyzer.VoipCall)
            {
                VoipCall call = e.ParsedItem as VoipCall;
                var callPresentation = VoipCallPresentation.FromAnalyzerVoipCall(call);
                if (_voipCalls.Contains(callPresentation))
                {
                    callPresentation.GetType().GetProperty(e.PropertyChanged.Name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).SetValue(_voipCalls.Where(c => c.Equals(callPresentation)).FirstOrDefault(), e.NewPropertyValue);
                }
            }
        }

        private void OnParsedItemDetected(object sender, ParsedItemDetectedEventArgs e)
        {
            if (e.ParsedItem is PcapAnalyzer.NetworkPassword)
            {
                if (_passwords.Add(e.ParsedItem as PcapAnalyzer.NetworkPassword))
                {
                    PrintDetectedItem(e.ParsedItem);
                }
            }
            else if (e.ParsedItem is PcapAnalyzer.NetworkHash)
            {
                if (_hashes.Add(e.ParsedItem as PcapAnalyzer.NetworkHash))
                {
                    PrintDetectedItem(e.ParsedItem);
                }
            }
            else if (e.ParsedItem is PcapAnalyzer.NetworkFile)
            {
                if (_extractedFiles.Add(e.ParsedItem as PcapAnalyzer.NetworkFile))
                {
                    PrintDetectedItem(e.ParsedItem);
                }
            }
            else if (e.ParsedItem is PcapAnalyzer.NetworkConnection)
            {
                var networkConnection = e.ParsedItem as NetworkConnection;
                _connections.Add(networkConnection);
            }
            else if (e.ParsedItem is PcapAnalyzer.VoipCall)
            {
                var voipCall = e.ParsedItem as VoipCall;
                VoipCallPresentation callPresentation = VoipCallPresentation.FromAnalyzerVoipCall(voipCall);
                PrintDetectedItem(callPresentation);
                _voipCalls.Add(callPresentation);
			}
            else if (e.ParsedItem is PcapAnalyzer.DnsNameMapping)
            {
                if (_dnsMappings.Add(e.ParsedItem as DnsNameMapping))
                {
                    PrintDetectedItem(e.ParsedItem);
                }

            }
        }

        private void PrintDetectedItem(object item)
        {
            Console.WriteLine($"Found: {item}");
        }
    }

}
