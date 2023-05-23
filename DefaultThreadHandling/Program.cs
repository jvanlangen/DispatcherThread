// See https://aka.ms/new-console-template for more information
using System.Threading.Tasks;

Console.WriteLine($"Main: {Thread.CurrentThread.ManagedThreadId}");


DispatcherThread thread = new DispatcherThread();

thread.Start();
await thread.Invoke(() => Console.WriteLine($"Test: {Thread.CurrentThread.ManagedThreadId}"));
await thread.Invoke(() => Console.WriteLine($"Test2: {Thread.CurrentThread.ManagedThreadId}"));


Console.WriteLine($"back to : {Thread.CurrentThread.ManagedThreadId}");

Console.ReadLine();