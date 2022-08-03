// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT

namespace TypeInjections.Test

open System.IO
open System.Reflection
open TypeInjections
open Microsoft.Dafny

module TestUtils =
    open NUnit.Framework
    open System

    type TestFormat = string -> string -> unit

    let pwd (testModule: string) : string =
        let wd = Environment.CurrentDirectory
        

        let pd =
            Directory
                .GetParent(
                    wd
                )
                .Parent
                .Parent
                .FullName

        Path.Combine(
            [| pd
               "Resources"
               TestContext.CurrentContext.Test.Name |]
        )

    let compare (actual: string) (expected: string) =
        if actual.Equals expected then
            ()
        else
            Console.WriteLine("Expected")
            Console.WriteLine(expected)
            Console.WriteLine("Actual")
            Console.WriteLine(actual)
            Assert.Fail()
            
    let fileFromPath (path : string) =
        let len = path.Length - 1
        let mutable i = len 
        while path[i] <> '/' do
            i <- i - 1
        
        path[i..]
        
        
    let ASTcompare (actual: YIL.Program) (expected: YIL.Program) =
        // can't compare actual to expect directly, the tok contains the file path of the program. Which will always be different
      
        
        if actual.Equals(expected) then
            ()
        else
            Console.WriteLine("Expected")
            // Console.WriteLine(expected)
            Console.WriteLine("Actual")
            // Console.WriteLine(actual)
            Assert.Fail()

    let fileCompare (actualFile: string) (expectedFile: string) =
        
        let expectedName = fileFromPath expectedFile
        let actualName = fileFromPath actualFile
        
        compare expectedName actualName
        
        let reporter = Program.initDafny
        
        let expected = Program.parseAST [FileInfo(expectedFile)] "compare" reporter
        let actual = Program.parseAST [FileInfo(actualFile)] "compare" reporter
        
        let eYIL = DafnyToYIL.program expected
        let aYIL = DafnyToYIL.program actual
        
        0 |> ignore

      //  ASTcompare aYIL eYIL
        
    let folderCompare(actualFolder: string) (expectedFolder: string) =
        
        
        // checking subfolders
        let actualSubs = DirectoryInfo(actualFolder).EnumerateDirectories("*", SearchOption.AllDirectories)
        let actualSubsName = Collections.Generic.List(List.map (fun (x: DirectoryInfo) -> x.Name) (actualSubs |> Seq.toList))
        let aSubs = actualSubsName |> Seq.toList
        
        let expectedSubs = DirectoryInfo(expectedFolder).EnumerateDirectories("*", SearchOption.AllDirectories)
        let expectedSubsName = Collections.Generic.List(List.map (fun (x : DirectoryInfo) -> x.Name) (expectedSubs |> Seq.toList))
        let eSubs = expectedSubsName |> Seq.toList
        
        // check subdirectory and file names
        if eSubs <> aSubs then
            Console.WriteLine("directory error")
            Console.WriteLine("Expected")
            Console.WriteLine(eSubs)
            Console.WriteLine("Actual")
            Console.WriteLine(aSubs)
            Assert.Fail()
            
        // get fileInfo dafny files from parent folder and all subdirectories
        let actualFiles = DirectoryInfo(actualFolder).EnumerateFiles("*.dfy", SearchOption.AllDirectories)
        let expectedFiles = DirectoryInfo(expectedFolder).EnumerateFiles("*.dfy", SearchOption.AllDirectories)
        
        
        // take sorted list of 'FileInfo' and compare the content of each file with 'fileCompare'
        List.iter2 (fun (x : FileInfo) (y : FileInfo) -> fileCompare x.FullName y .FullName) (expectedFiles |> Seq.toList) (actualFiles |> Seq.toList)
        

    // Run the tests for generated functions
    // FilePath is synonym for string list
    // the test generator expects multiple output and expected files,
    // but the tool currently generates only one output file
    let public testRunnerGen
        (testToRun: TestFormat)
        (testModule: string)
        (directoryName: string)
        (outputDirectoryName: string)
        (expectedDirectoryName: string)
        =
        let pwd = pwd testModule

        let inputDirectory =
            System.IO.Path.Combine([| pwd; directoryName |])

        let outputDirectory =
            System.IO.Path.Combine([| pwd; outputDirectoryName |])

        let expectedDirectory =
            System.IO.Path.Combine([| pwd; expectedDirectoryName |])

        //TypeInjections.Program.foo inputDirectory outputDirectory
        // TypeInjections.Program.main [|inputDirectory + "/Old"; inputDirectory + "/New"; outputDirectory|]
        // |> ignore
        
        testToRun outputDirectory expectedDirectory


