namespace SomeBasicFileStoreApp
open System
open System.Collections.Concurrent
open System.Threading

type PersistCommands(appendBatch:IAppendBatch)=
    let mutable thread= null
    let stop = ref false
    let commands = ConcurrentQueue<Command>()
    let signal = new EventWaitHandle(false, EventResetMode.AutoReset)
    
    let appendBatch()=
        let receivedCommands = ResizeArray<Command>()

        let command= ref Command.Empty
        while (commands.TryDequeue(command)) do
            receivedCommands.Add(!command)

        Async.RunSynchronously(appendBatch.Batch(receivedCommands |> Seq.toList)) |> ignore

    member this.ThreadStart():unit=
        while (not !stop) do
            signal.WaitOne() |> ignore
            appendBatch()
        // While the batch has been running, more commands might have been added
        // and stop might have been called
        appendBatch()

    member this.Start()=
        if (thread <> null) then
            failwith("already started")
        else
            thread <- Thread(this.ThreadStart)
            thread.Start()

    member __.Started()=
        thread<>null

    member __.Stop()=
        stop := true
        signal.Set() |> ignore

        if (thread <> null) then
            thread.Join()
        else
            ()

    member __.Handle(command)=
        // send the command to separate thread and persist it
        commands.Enqueue(command)
        signal.Set() |> ignore
    interface IDisposable with
        member __.Dispose()=signal.Dispose()