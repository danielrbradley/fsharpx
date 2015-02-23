namespace FSharpx.Functional.Tests

open System
open System.Threading
open System.Threading.Tasks

open FSharpx.Control

open FsUnit
open NUnit.Framework

[<TestFixture>]
type ``AsyncSeq module Tests``() = 

    [<Test>]
    member test.``skipping should return all elements after the first non-match``() =
         let expected = [ 3; 4 ]
         let result = 
            [ 1; 2; 3; 4 ] 
            |> AsyncSeq.ofSeq 
            |> AsyncSeq.skipWhile (fun i -> i <= 2) 
            |> AsyncSeq.toBlockingSeq 
            |> Seq.toList
         result |> should equal expected
