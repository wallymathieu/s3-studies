namespace CoreFsTests

open Xunit
open System.IO
open System
open System.Collections.Generic

open Amazon.S3

open Amazon

open SomeBasicFileStoreApp
open Helpers
open GetCommands
module S3=
    open Amazon.S3.Model

    let createBucket bucketName (s3:AmazonS3Client)=
        let req = PutBucketRequest()
        req.BucketName <- bucketName
        req.UseClientRegion <- true
        s3.PutBucketAsync(req)
    let deleteBucket bucketName (s3:AmazonS3Client)=
        let req = DeleteBucketRequest()
        req.BucketName <- bucketName
        req.UseClientRegion <- true
        s3.DeleteBucketAsync(req)

// https://www.minio.io/kubernetes.html
// https://docs.minio.io/docs/how-to-use-aws-sdk-for-net-with-minio-server.html
type PersistingEventsTests() = 
    let s3Factory ()=
        let accessKey="testkey"
        let secretKey="secretkey"
        let config = AmazonS3Config()
        // MUST set this before setting ServiceURL and it should match the `MINIO_REGION` enviroment variable.
        config.RegionEndpoint <- RegionEndpoint.USEast1
        // replace http://localhost:9000 with URL of your minio server
        config.ServiceURL <- "http://localhost:9000"
        // MUST be true to work correctly with Minio server
        config.ForcePathStyle <- true 
        new AmazonS3Client(accessKey, secretKey, config)

    [<Fact>]
    member this.Read_items_persisted_in_single_batch()=Async.StartAsTask(async{
        let bucket = "test-1-json-container-"+Guid.NewGuid().ToString("N")
        let! _ =
            use s3 = s3Factory() 
            Async.AwaitTask(S3.createBucket bucket s3)
        let commands = getCommands()
        let _persist = JsonAppendToBucket(bucket, 10, s3Factory) :> IAppendBatch
        let! _= _persist.Batch(commands |> unwrap)
        let! persistedCommands = _persist.ReadAll()
        Assert.Equal(persistedCommands |> List.length, commands.Length)
    })

