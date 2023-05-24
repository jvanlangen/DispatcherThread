// See https://aka.ms/new-console-template for more information
using System.Threading.Tasks;

Console.WriteLine($"Main: {Thread.CurrentThread.ManagedThreadId}");


DispatcherThread thread = new DispatcherThread();


var res = await thread.Invoke(async () => {
    await Task.Delay(1000);
    Console.WriteLine($"Test: {Thread.CurrentThread.ManagedThreadId}");
    return "blabla";
    });
await thread.Invoke(() => Console.WriteLine($"Test2: {Thread.CurrentThread.ManagedThreadId}"));


Console.WriteLine($"back to : {Thread.CurrentThread.ManagedThreadId}");

Console.ReadLine();