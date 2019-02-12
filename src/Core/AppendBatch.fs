namespace SomeBasicFileStoreApp
open System
open System.Collections.Generic
open System.IO
open Amazon
open Amazon.S3
open Amazon.S3.Model
open System.Threading.Tasks

type IAppendBatch=
    abstract member Batch: Command list->Async<int64>
    abstract member ReadAll: unit-> Async<Command list>

type JsonAppendToBucket(bucketName, maxKeys, s3Factory:(unit -> AmazonS3Client))=
    interface IAppendBatch with

        member this.Batch cs=
            let addBatch (c:AmazonS3Client) = 
                let req = PutObjectRequest()
                req.BucketName <- bucketName
                req.Key <- sprintf "cmds_%O.json" (Guid.NewGuid())
                let ms = new MemoryStream()
                let w = new StreamWriter(ms)
                w.Write(JsonConvertCommands.serialize(cs |> List.toArray))
                w.Flush()
                ms.Seek(0L, SeekOrigin.Begin) |> ignore
                req.InputStream <- ms
                Async.AwaitTask(c.PutObjectAsync(req))
                
            async{
                use s3 = s3Factory()
                let! r=addBatch s3
                return r.ContentLength 
            }
            

        member this.ReadAll ()=
            let readFromStream (s:Stream)=
                use r = new StreamReader(s)
                JsonConvertCommands.deserialize<Command array>(r.ReadToEnd())
                |> Seq.toArray
            let readBatches () = 
                let readObjects (req:ListObjectsRequest)=async{ 
                    use s3 = s3Factory()
                    let! objects =Async.AwaitTask(s3.ListObjectsAsync(req))
                    let! items=
                        objects.S3Objects 
                              |> Seq.map (fun o->
                                let req = GetObjectRequest()
                                req.BucketName <- o.BucketName
                                req.Key <- o.Key;
                                s3.GetObjectAsync(req))
                              |> Task.WhenAll
                              |> Async.AwaitTask
                    let commands = items|> Array.collect (fun i->readFromStream i.ResponseStream)
                    if objects.IsTruncated then
                        let nextReq = ListObjectsRequest()
                        nextReq.BucketName <- req.BucketName
                        nextReq.MaxKeys <- req.MaxKeys
                        nextReq.Marker <- objects.NextMarker
                        nextReq.Prefix <- req.Prefix
                        return (commands, Some nextReq)
                    else
                        return (commands, None)
                }
                async {
                    let req = ListObjectsRequest()
                    req.Prefix <- "cmds_"
                    req.BucketName <- bucketName
                    req.MaxKeys <- maxKeys
                    
                    let! (commands,maybeNext') = readObjects req
                    let allCommands= List<_>()
                    allCommands.Add commands
                    let mutable maybeNext = maybeNext'
                    while (maybeNext.IsSome) do 
                        match maybeNext with
                        | Some next ->
                             let! (commands,maybeNext') = readObjects req
                             allCommands.Add commands
                             maybeNext <- maybeNext'
                        | None -> ()
                    return allCommands.ToArray() |> Array.concat |> Array.toList 
                }            
            readBatches()
