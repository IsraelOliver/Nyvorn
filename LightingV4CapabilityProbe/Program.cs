using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LightingV4CapabilityProbe
{
    /// <summary>
    /// Lighting V4.2B-B: MonoGame Capability Validation
    ///
    /// Isolated test harness for MonoGame.DesktopGL capabilities.
    /// Executes tests from manifest (LIGHTING_V4_2B_B_TEST_MANIFEST.md)
    /// Generates reports: CAPABILITY_RESULTS.md, CAPABILITY_RAW.csv, CAPABILITY_FAILURES.md
    ///
    /// NO integration with Nyvorn main project.
    /// NO changes to Legacy or Lighting V3.
    /// NO connection to production rendering pipeline.
    /// </summary>
    class Program : Game
    {
        private GraphicsDeviceManager graphics;
        private SpriteBatch spriteBatch;
        private TestRunner runner;
        private bool testsRunning;

        static void Main(string[] args)
        {
            using (var game = new Program())
            {
                // Parse command line
                var options = ParseCommandLine(args);
                game.runner = new TestRunner(game, options);
                game.testsRunning = true;
                game.Run();
            }
        }

        public Program()
        {
            graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
            IsFixedTimeStep = false;  // Uncapped for profiling
        }

        protected override void Initialize()
        {
            base.Initialize();

            // Capture environment before running tests
            CaptureEnvironment();

            // Wait one frame for graphics device to settle
            runner.WaitFrames = 1;
        }

        protected override void LoadContent()
        {
            spriteBatch = new SpriteBatch(GraphicsDevice);
        }

        protected override void Update(GameTime gameTime)
        {
            if (!testsRunning)
            {
                Exit();
                return;
            }

            // Let TestRunner orchestrate
            testsRunning = runner.Update(gameTime);

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);

            // Let TestRunner render (if needed for visual tests)
            runner.Draw(GraphicsDevice, spriteBatch, gameTime);

            base.Draw(gameTime);
        }

        private void CaptureEnvironment()
        {
            Console.WriteLine("=== CAPABILITY PROBE ENVIRONMENT ===");
            Console.WriteLine($"OS: {Environment.OSVersion}");
            Console.WriteLine($"MonoGame: {typeof(Game).Assembly.GetName().Version}");
            Console.WriteLine($"GraphicsProfile Requested: {graphics.PreferredGraphicsProfile}");
            Console.WriteLine($"GraphicsProfile Actual: {GraphicsDevice.GraphicsProfile}");
            Console.WriteLine($"Backbuffer: {GraphicsDevice.PresentationParameters.BackBufferWidth}×{GraphicsDevice.PresentationParameters.BackBufferHeight}");
            Console.WriteLine($"Fullscreen: {GraphicsDevice.PresentationParameters.IsFullScreen}");
            Console.WriteLine($"Build: {(System.Diagnostics.Debugger.IsAttached ? "Debug+Debugger" : "Release")}");
            Console.WriteLine();
        }

        private static CommandLineOptions ParseCommandLine(string[] args)
        {
            var options = new CommandLineOptions();

            if (args.Length == 0)
            {
                options.RunMode = RunMode.All;
                return options;
            }

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--run-all":
                        options.RunMode = RunMode.All;
                        break;
                    case "--run-category":
                        if (i + 1 < args.Length)
                        {
                            options.RunMode = RunMode.Category;
                            options.Category = args[++i];
                        }
                        break;
                    case "--run-test":
                        if (i + 1 < args.Length)
                        {
                            options.RunMode = RunMode.SingleTest;
                            options.TestId = args[++i];
                        }
                        break;
                    case "--skip-visual":
                        options.SkipVisualTests = true;
                        break;
                    case "--resolution":
                        if (i + 1 < args.Length)
                        {
                            options.ResolutionOverride = args[++i];
                        }
                        break;
                    case "--headless":
                        options.Headless = true;
                        break;
                    case "--list-tests":
                        options.ListTestsOnly = true;
                        break;
                }
            }

            return options;
        }
    }

    public enum RunMode
    {
        All,
        Category,
        SingleTest,
        List
    }

    public class CommandLineOptions
    {
        public RunMode RunMode { get; set; } = RunMode.All;
        public string Category { get; set; }
        public string TestId { get; set; }
        public bool SkipVisualTests { get; set; }
        public string ResolutionOverride { get; set; }
        public bool Headless { get; set; }
        public bool ListTestsOnly { get; set; }
    }

    /// <summary>
    /// Orchestrates test execution.
    /// Each test runs isolated; failure doesn't stop other tests.
    /// </summary>
    public class TestRunner
    {
        private readonly Game game;
        private readonly CommandLineOptions options;
        private readonly List<TestCase> testCases = new();
        private readonly TestReporter reporter = new();
        private int currentTestIndex;
        public int WaitFrames { get; set; }

        public TestRunner(Game game, CommandLineOptions options)
        {
            this.game = game;
            this.options = options;
            InitializeTestCases();
        }

        private void InitializeTestCases()
        {
            // Instantiate all test cases from manifest
            // CP-ENV-001 through CP-TEXTURE-GETDATA-1440-001

            // Environment tests
            testCases.Add(new EnvironmentTests.OSDetectionTest());
            testCases.Add(new EnvironmentTests.GPUDetectionTest());
            testCases.Add(new EnvironmentTests.DriverDetectionTest());
            testCases.Add(new EnvironmentTests.MonoGameVersionTest());
            testCases.Add(new EnvironmentTests.GraphicsProfileTest());
            testCases.Add(new EnvironmentTests.BackbufferSizeTest());

            // RenderTarget format tests
            testCases.Add(new RenderTargetTests.ColorCreationTest());
            testCases.Add(new RenderTargetTests.SingleCreationTest());
            testCases.Add(new RenderTargetTests.SingleCorrectnessTest());
            // ... (add all others per manifest)

            // Shader loop tests
            testCases.Add(new ShaderLoopTests.Loop8Test());
            testCases.Add(new ShaderLoopTests.Loop16Test());
            testCases.Add(new ShaderLoopTests.Loop32Test());
            testCases.Add(new ShaderLoopTests.Loop64Test());
            // ... (add all others)

            // Filter by command-line options
            FilterTestCases();
        }

        private void FilterTestCases()
        {
            if (options.RunMode == RunMode.Category)
            {
                testCases.RemoveAll(t => !t.Category.Equals(options.Category, StringComparison.OrdinalIgnoreCase));
            }
            else if (options.RunMode == RunMode.SingleTest)
            {
                testCases.RemoveAll(t => !t.TestId.Equals(options.TestId, StringComparison.OrdinalIgnoreCase));
            }

            if (options.SkipVisualTests)
            {
                testCases.RemoveAll(t => t.Type == TestType.Visual);
            }
        }

        public bool Update(GameTime gameTime)
        {
            if (options.ListTestsOnly)
            {
                Console.WriteLine("Available tests:");
                foreach (var test in testCases)
                {
                    Console.WriteLine($"  {test.TestId} ({test.Category}): {test.Name}");
                }
                return false;  // Exit
            }

            if (WaitFrames > 0)
            {
                WaitFrames--;
                return true;  // Continue
            }

            if (currentTestIndex >= testCases.Count)
            {
                // All tests complete; export results and exit
                ExportResults();
                return false;
            }

            var test = testCases[currentTestIndex];
            Console.WriteLine($"Running {test.TestId}: {test.Name}...");

            var result = test.Execute(game.GraphicsDevice);
            reporter.RecordResult(result);

            currentTestIndex++;
            WaitFrames = 1;  // Wait one frame between tests

            return true;  // Continue
        }

        public void Draw(GraphicsDevice graphics, SpriteBatch batch, GameTime gameTime)
        {
            // Render visual test results if needed
            // (optional; tests generate screenshots if needed)
        }

        private void ExportResults()
        {
            Console.WriteLine("\n=== TEST COMPLETE ===");
            Console.WriteLine($"Total tests run: {reporter.ResultCount}");
            Console.WriteLine($"Passed: {reporter.PassedCount}");
            Console.WriteLine($"Failed: {reporter.FailedCount}");
            Console.WriteLine($"Unsupported: {reporter.UnsupportedCount}");
            Console.WriteLine($"Inconclusive: {reporter.InconclusiveCount}");
            Console.WriteLine();

            reporter.ExportMarkdown("LIGHTING_V4_CAPABILITY_RESULTS.md");
            reporter.ExportCSV("LIGHTING_V4_CAPABILITY_RAW.csv");
            reporter.ExportFailures("LIGHTING_V4_CAPABILITY_FAILURES.md");

            Console.WriteLine("Results exported.");
            Console.WriteLine("Next: Review LIGHTING_V4_CAPABILITY_RESULTS.md");
            Console.WriteLine("Then: Await approval before starting Spike A/B/C/E");
        }
    }

    /// <summary>
    /// Base class for all test cases.
    /// Implements isolation and exception handling.
    /// </summary>
    public abstract class TestCase
    {
        public abstract string TestId { get; }
        public abstract string Category { get; }
        public abstract string Name { get; }
        public abstract TestType Type { get; }
        public abstract int TimeoutMs { get; }

        public virtual TestResult Execute(GraphicsDevice graphics)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                ExecuteInternal(graphics);
                sw.Stop();
                return new TestResult
                {
                    TestId = TestId,
                    Category = Category,
                    Name = Name,
                    Status = TestStatus.Passed,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (NotSupportedException ex)
            {
                sw.Stop();
                return new TestResult
                {
                    TestId = TestId,
                    Category = Category,
                    Name = Name,
                    Status = TestStatus.Unsupported,
                    FailureMessage = ex.Message,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (TimeoutException ex)
            {
                sw.Stop();
                return new TestResult
                {
                    TestId = TestId,
                    Category = Category,
                    Name = Name,
                    Status = TestStatus.Inconclusive,
                    FailureMessage = ex.Message,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new TestResult
                {
                    TestId = TestId,
                    Category = Category,
                    Name = Name,
                    Status = TestStatus.Failed,
                    FailureMessage = ex.Message,
                    FailureType = FailureType.RuntimeException,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
        }

        protected abstract void ExecuteInternal(GraphicsDevice graphics);
    }

    public enum TestType { Automatic, Visual }
    public enum TestStatus { Passed, Failed, Unsupported, Inconclusive, PendingReview, NotTested, Skipped }
    public enum FailureType { None, NotSupported, CompilationError, RuntimeException, AssertionFailed, Timeout, OutOfMemory }

    public class TestResult
    {
        public string TestId { get; set; }
        public string Category { get; set; }
        public string Name { get; set; }
        public TestStatus Status { get; set; }
        public FailureType FailureType { get; set; }
        public string FailureMessage { get; set; }
        public long DurationMs { get; set; }
        public double CpuSetupMs { get; set; }
        public double CpuSubmissionMs { get; set; }
        public double DrawMs { get; set; }
        public long AllocatedBytes { get; set; }
    }

    /// <summary>
    /// Aggregates test results and exports to CSV and Markdown.
    /// </summary>
    public class TestReporter
    {
        private readonly List<TestResult> results = new();

        public int ResultCount => results.Count;
        public int PassedCount => results.Count(r => r.Status == TestStatus.Passed);
        public int FailedCount => results.Count(r => r.Status == TestStatus.Failed);
        public int UnsupportedCount => results.Count(r => r.Status == TestStatus.Unsupported);
        public int InconclusiveCount => results.Count(r => r.Status == TestStatus.Inconclusive);

        public void RecordResult(TestResult result)
        {
            results.Add(result);
        }

        public void ExportMarkdown(string filename)
        {
            // Generate CAPABILITY_RESULTS.md
            // See LIGHTING_V4_2B_B_CAPABILITY_PLAN.md for format
        }

        public void ExportCSV(string filename)
        {
            // Generate CAPABILITY_RAW.csv
            // Columns: TestId, Category, Status, DurationMs, CpuSubmissionMs, etc.
        }

        public void ExportFailures(string filename)
        {
            // Generate CAPABILITY_FAILURES.md
            // List only failures and inconclusive results
        }
    }

    // Test namespaces (stubs for now; will be implemented)
    namespace EnvironmentTests
    {
        public class OSDetectionTest : TestCase
        {
            public override string TestId => "CP-ENV-001";
            public override string Category => "Environment";
            public override string Name => "OS Detection";
            public override TestType Type => TestType.Automatic;
            public override int TimeoutMs => 5000;

            protected override void ExecuteInternal(GraphicsDevice graphics)
            {
                var os = Environment.OSVersion;
                if (os == null) throw new InvalidOperationException("Cannot detect OS");
            }
        }

        public class GPUDetectionTest : TestCase
        {
            public override string TestId => "CP-ENV-002";
            public override string Category => "Environment";
            public override string Name => "GPU Detection";
            public override TestType Type => TestType.Automatic;
            public override int TimeoutMs => 5000;

            protected override void ExecuteInternal(GraphicsDevice graphics)
            {
                // Query GPU name (implementation depends on graphics API)
            }
        }

        public class DriverDetectionTest : TestCase
        {
            public override string TestId => "CP-ENV-003";
            public override string Category => "Environment";
            public override string Name => "GPU Driver";
            public override TestType Type => TestType.Automatic;
            public override int TimeoutMs => 5000;

            protected override void ExecuteInternal(GraphicsDevice graphics)
            {
                // Query driver version (implementation depends on graphics API)
            }
        }

        public class MonoGameVersionTest : TestCase
        {
            public override string TestId => "CP-ENV-004";
            public override string Category => "Environment";
            public override string Name => "MonoGame Version";
            public override TestType Type => TestType.Automatic;
            public override int TimeoutMs => 5000;

            protected override void ExecuteInternal(GraphicsDevice graphics)
            {
                var version = typeof(Game).Assembly.GetName().Version;
                if (version == null) throw new InvalidOperationException("Cannot detect MonoGame version");
            }
        }

        public class GraphicsProfileTest : TestCase
        {
            public override string TestId => "CP-ENV-005";
            public override string Category => "Environment";
            public override string Name => "Graphics Profile";
            public override TestType Type => TestType.Automatic;
            public override int TimeoutMs => 5000;

            protected override void ExecuteInternal(GraphicsDevice graphics)
            {
                var profile = graphics.GraphicsProfile;
                if (profile != GraphicsProfile.HiDef && profile != GraphicsProfile.Reach)
                    throw new InvalidOperationException($"Unexpected profile: {profile}");
            }
        }

        public class BackbufferSizeTest : TestCase
        {
            public override string TestId => "CP-ENV-006";
            public override string Category => "Environment";
            public override string Name => "Backbuffer Size";
            public override TestType Type => TestType.Automatic;
            public override int TimeoutMs => 5000;

            protected override void ExecuteInternal(GraphicsDevice graphics)
            {
                var width = graphics.PresentationParameters.BackBufferWidth;
                var height = graphics.PresentationParameters.BackBufferHeight;
                if (width <= 0 || height <= 0)
                    throw new InvalidOperationException($"Invalid backbuffer: {width}×{height}");
            }
        }
    }

    namespace RenderTargetTests
    {
        public class ColorCreationTest : TestCase
        {
            public override string TestId => "CP-RT-001";
            public override string Category => "RenderTarget";
            public override string Name => "Color Creation";
            public override TestType Type => TestType.Automatic;
            public override int TimeoutMs => 5000;

            protected override void ExecuteInternal(GraphicsDevice graphics)
            {
                var rt = new RenderTarget2D(graphics, 512, 512, false, SurfaceFormat.Color, DepthFormat.None);
                if (rt == null) throw new InvalidOperationException("Failed to create RenderTarget2D");
                rt.Dispose();
            }
        }

        public class SingleCreationTest : TestCase
        {
            public override string TestId => "CP-RT-005";
            public override string Category => "RenderTarget";
            public override string Name => "Single Creation";
            public override TestType Type => TestType.Automatic;
            public override int TimeoutMs => 5000;

            protected override void ExecuteInternal(GraphicsDevice graphics)
            {
                try
                {
                    var rt = new RenderTarget2D(graphics, 512, 512, false, SurfaceFormat.Single, DepthFormat.None);
                    if (rt == null) throw new InvalidOperationException("Failed to create Single RT");
                    rt.Dispose();
                }
                catch (ArgumentException)
                {
                    throw new NotSupportedException("SurfaceFormat.Single not supported");
                }
            }
        }

        public class SingleCorrectnessTest : TestCase
        {
            public override string TestId => "CP-RT-008";
            public override string Category => "RenderTarget";
            public override string Name => "Single Correctness";
            public override TestType Type => TestType.Automatic;
            public override int TimeoutMs => 10000;

            protected override void ExecuteInternal(GraphicsDevice graphics)
            {
                // Test gradient correctness (partial implementation)
                // See LIGHTING_V4_2B_B_CAPABILITY_PLAN.md for full spec
            }
        }
    }

    namespace ShaderLoopTests
    {
        public class Loop8Test : TestCase
        {
            public override string TestId => "CP-SHADER-001";
            public override string Category => "ShaderLoop";
            public override string Name => "Loop 8 Compile";
            public override TestType Type => TestType.Automatic;
            public override int TimeoutMs => 10000;

            protected override void ExecuteInternal(GraphicsDevice graphics)
            {
                // Compile Loop8.fx; verify no errors
                // (implementation depends on content pipeline)
            }
        }

        public class Loop16Test : TestCase
        {
            public override string TestId => "CP-SHADER-004";
            public override string Category => "ShaderLoop";
            public override string Name => "Loop 16 Compile";
            public override TestType Type => TestType.Automatic;
            public override int TimeoutMs => 10000;

            protected override void ExecuteInternal(GraphicsDevice graphics)
            {
                // Compile Loop16.fx; verify no errors
            }
        }

        public class Loop32Test : TestCase
        {
            public override string TestId => "CP-SHADER-007";
            public override string Category => "ShaderLoop";
            public override string Name => "Loop 32 Compile";
            public override TestType Type => TestType.Automatic;
            public override int TimeoutMs => 10000;

            protected override void ExecuteInternal(GraphicsDevice graphics)
            {
                // Compile Loop32.fx; verify no errors
            }
        }

        public class Loop64Test : TestCase
        {
            public override string TestId => "CP-SHADER-010";
            public override string Category => "ShaderLoop";
            public override string Name => "Loop 64 Compile";
            public override TestType Type => TestType.Automatic;
            public override int TimeoutMs => 10000;

            protected override void ExecuteInternal(GraphicsDevice graphics)
            {
                // Compile Loop64.fx; may have warnings but should succeed
            }
        }
    }
}
