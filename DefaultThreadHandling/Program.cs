// See https://aka.ms/new-console-template for more information
using PWR.Common.Threading;
using System.Threading.Tasks;

Console.WriteLine($"Main: {Thread.CurrentThread.ManagedThreadId}");


SingleThread thread = new();
SingleThread thread2 = new();
thread.ThreadName = "BLABLA";

thread2.Post(() => Console.WriteLine($"Pino: {Environment.CurrentManagedThreadId}"));

var res = await thread.SendAsync(async () =>
{
    Console.WriteLine($"Test A: {Environment.CurrentManagedThreadId}");
    await Task.Delay(1000);
    await thread2.SendAsync(() => Console.WriteLine($"Test A 2: {Thread.CurrentThread.ManagedThreadId}"));
    Console.WriteLine($"Test B: {Environment.CurrentManagedThreadId}");

    await thread2.SendAsync(() => Console.WriteLine($"Test B 2: {Thread.CurrentThread.ManagedThreadId}"));
    return "blabla";
});

Console.WriteLine($"Execution delay Test2: {Environment.CurrentManagedThreadId}");

await thread.SendAsync(TimeSpan.FromSeconds(2), () =>
{
    Console.WriteLine($"Test2: {Environment.CurrentManagedThreadId}");
});

await thread.SendAsync(() =>
{
    Console.WriteLine($"Test3: {Environment.CurrentManagedThreadId}");
});

Console.WriteLine($"back to : {Environment.CurrentManagedThreadId}");