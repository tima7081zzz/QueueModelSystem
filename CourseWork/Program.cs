﻿using System.Collections.Concurrent;

namespace CourseWork;

public class ClientRequest
{
    public int Id { get; set; }
    public bool IsNeedToRepairDetails { get; set; }
    public DateTime CreatedTime { get; set; }
    public DateTime? ProcessedTime { get; set; }
}

public class IdGenerator
{
    private int _id;

    public int Generate()
    {
        return Interlocked.Increment(ref _id);
    }
}

public class RequestGenerator
{
    private readonly ConcurrentBag<ClientRequest> _generatedRequests = new();
    private readonly RegularQueue _queueRegular;
    private readonly IdGenerator _idGenerator;

    public RequestGenerator(RegularQueue queueRegular, IdGenerator idGenerator)
    {
        _queueRegular = queueRegular;
        _idGenerator = idGenerator;
    }

    public async Task GenerateRequests(double repairDetailsPercentage, CancellationToken ct)
    {
        var rnd = new Random();

        while (!ct.IsCancellationRequested)
        {
            var request = new ClientRequest
            {
                Id = _idGenerator.Generate(),
                IsNeedToRepairDetails = rnd.Next(100) < repairDetailsPercentage,
                CreatedTime = DateTime.Now,
            };

            ClientRequestLogger.RequestCreated(request);
            _queueRegular.Enqueue(request);
            _generatedRequests.Add(request);

            await Task.Delay(250, ct);
        }
    }

    public IList<ClientRequest> GetGeneratedRequests()
    {
        return _generatedRequests.ToList();
    }
}

public class RegularQueue
{
    private readonly ConcurrentQueue<ClientRequest> _queueRegular = new();
    private readonly AdditionalServiceQueue _queueAdditionalService;
    private readonly ConcurrentBag<ClientRequest> _processedRequests = new();

    public RegularQueue(AdditionalServiceQueue queueAdditionalService)
    {
        _queueAdditionalService = queueAdditionalService;
    }

    public void Enqueue(ClientRequest request)
    {
        _queueRegular.Enqueue(request);
    }

    public async Task StartProcessing(CancellationToken ct)
    {
        var isNeedToEndProcessing = true;
        while (!ct.IsCancellationRequested || isNeedToEndProcessing)
        {
            if (ct.IsCancellationRequested)
            {
                isNeedToEndProcessing = false;
            }

            if (!_queueRegular.TryDequeue(out var request))
            {
                continue;
            }

            //ProcessingImitation
            await Task.Delay(500, CancellationToken.None);

            request.ProcessedTime = DateTime.Now;

            _processedRequests.Add(request);
            ClientRequestLogger.RequestProcessedRegularQueue(request);

            if (!request.IsNeedToRepairDetails)
            {
                continue;
            }

            _queueAdditionalService.Enqueue(request);
        }
    }

    public IList<ClientRequest> GetProcessedRequests()
    {
        return _processedRequests.ToList();
    }
}

public class AdditionalServiceQueue
{
    private readonly ConcurrentQueue<ClientRequest> _queue = new();
    private readonly ConcurrentBag<ClientRequest> _processedRequests = new();

    public void Enqueue(ClientRequest request)
    {
        _queue.Enqueue(request);
    }

    public async Task StartProcessing(CancellationToken ct)
    {
        var isNeedToEndProcessing = true;
        while (!ct.IsCancellationRequested || isNeedToEndProcessing)
        {
            if (ct.IsCancellationRequested)
            {
                isNeedToEndProcessing = false;
            }

            if (!_queue.TryDequeue(out var request))
            {
                continue;
            }

            //ProcessingImitation
            await Task.Delay(700, CancellationToken.None);

            request.ProcessedTime = DateTime.Now;

            ClientRequestLogger.RequestProcessedAdditionalServiceQueue(request);
            _processedRequests.Add(request);
        }
    }

    public IList<ClientRequest> GetProcessedRequests()
    {
        return _processedRequests.ToList();
    }
}

public static class ClientRequestLogger
{
    public static void RequestCreated(ClientRequest request)
    {
        Console.WriteLine(
            $"{DateTime.Now}  | {GetRequestInfoPart(request.IsNeedToRepairDetails)} Request created: ID = {request.Id}");
    }

    public static void RequestProcessedRegularQueue(ClientRequest request)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(
            $"{DateTime.Now}  | {GetRequestInfoPart(request.IsNeedToRepairDetails)} Request processed in regular queue: ID = {request.Id}");
        Console.ResetColor();
    }

    public static void RequestProcessedAdditionalServiceQueue(ClientRequest request)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(
            $"{DateTime.Now}  | {GetRequestInfoPart(request.IsNeedToRepairDetails)} Request processed in additional service queue: ID = {request.Id}");
        Console.ResetColor();
    }

    public static void ShowOverallStatistics(
        IList<ClientRequest> generated,
        IList<ClientRequest> processedRegular,
        IList<ClientRequest> processedAdditionalService)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n\n--- GENERATED COUNT ---" +
                          $"\nTotal: {generated.Count}" +
                          $"\nRegular: {generated.Count(x => !x.IsNeedToRepairDetails)}" +
                          $"\nAdditionalService: {generated.Count(x => x.IsNeedToRepairDetails)}");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine("\n\n--- PROCESSED COUNT ---" +
                          $"\nRegular: {processedRegular.Count}" +
                          $"\nAdditionalService: {processedAdditionalService.Count}");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("\n\n--- PROCESSING TIME ---" +
                          $"\nRegular: {GetAverageProcessingTime(processedRegular):g}" +
                          $"\nAdditionalService: {GetAverageProcessingTime(processedAdditionalService):g}");
        Console.ResetColor();

        Console.ReadKey();
    }

    private static TimeSpan GetAverageProcessingTime(ICollection<ClientRequest> requests)
    {
        if (!requests.Any())
        {
            return TimeSpan.Zero;
        }

        var averageTicks = requests
            .Where(x => x.ProcessedTime.HasValue)
            .Select(x => x.ProcessedTime!.Value.Ticks - x.CreatedTime.Ticks)
            .Average();

        return new TimeSpan(Convert.ToInt64(averageTicks));
    }

    private static string GetRequestInfoPart(bool isNeedToRepairDetails)
    {
        return isNeedToRepairDetails ? "Additional Service" : "Regular";
    }
}

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var (ct, repairDetailsPercentage, numberOfChannels) = GetProgramData();

        var additionalServiceQueue = new AdditionalServiceQueue();
        var queueRegular = new RegularQueue(additionalServiceQueue);
        var requestsGenerator = new RequestGenerator(queueRegular, new IdGenerator());

        var programTasks = new List<Task>
        {
            Task.Run(() => requestsGenerator.GenerateRequests(repairDetailsPercentage, ct), ct)
        };

        for (var i = 0; i < numberOfChannels; i++)
        {
            programTasks.Add(Task.Run(() => queueRegular.StartProcessing(ct), ct));
            programTasks.Add(Task.Run(() => additionalServiceQueue.StartProcessing(ct), ct));
        }

        try
        {
            await Task.WhenAll(programTasks);
        }
        finally
        {
            var generated = requestsGenerator.GetGeneratedRequests();
            var processedRegular = queueRegular.GetProcessedRequests();
            var processedAdditionalService = additionalServiceQueue.GetProcessedRequests();
            ClientRequestLogger.ShowOverallStatistics(generated, processedRegular, processedAdditionalService);
        }
    }

    private static (CancellationToken Ct, double RepairDetailsPercentage, int NumberOfChannels) GetProgramData()
    {
        Console.WriteLine("(Time is in seconds)");
        Console.Write("Enter simulation time: ");
        var simulationTimeSeconds = TimeSpan.FromSeconds(int.Parse(Console.ReadLine()!));


        Console.Write("\nEnter regular clients percentage: ");
        var repairDetailsPercentage = double.Parse(Console.ReadLine()!);

        Console.Write("\nEnter number of processing servers: ");
        var numberOfChannels = int.Parse(Console.ReadLine()!);

        var ct = new CancellationTokenSource(simulationTimeSeconds).Token;
        return (ct, repairDetailsPercentage, numberOfChannels);
    }
}