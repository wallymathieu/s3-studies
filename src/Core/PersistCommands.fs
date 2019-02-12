﻿namespace SomeBasicFileStoreApp
open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading

type PersistCommands(appendBatch:IAppendBatch)=
    let mutable thread= null
    let stop = ref false
    let commands = new ConcurrentQueue<Command>()
    let signal = new EventWaitHandle(false, EventResetMode.AutoReset)
    
    let AppendBatch()=
        let receivedCommands = new List<Command>()

        let command= ref Command.Empty
        while (commands.TryDequeue(command)) do
            receivedCommands.Add(!command)

        Async.RunSynchronously(appendBatch.Batch(receivedCommands |> Seq.toList)) |> ignore

    member this.ThreadStart():unit=
        while (not !stop) do
            signal.WaitOne() |> ignore
            AppendBatch()
        // While the batch has been running, more commands might have been added
        // and stop might have been called
        AppendBatch()

    member this.Start()=
        if (thread <> null) then
            failwith("already started")
        else
            thread <- new Thread(this.ThreadStart)
            thread.Start()

    member this.Started()=
        thread<>null

    member this.Stop()=
        stop := true
        signal.Set() |> ignore

        if (thread <> null) then
            thread.Join()
        else
            ()

    member this.Handle(command)=
        // send the command to separate thread and persist it
        commands.Enqueue(command)
        signal.Set() |> ignore