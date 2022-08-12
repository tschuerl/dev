namespace TypeInjections
open System
open System.IO
open System.Text.RegularExpressions



///
/// TypecartAPI exposes Typecart functionalities as a reusable component.
///
module Typecart =
    
    /// Interface for handling how typeCart writes out the diffed files
    type TypecartOutput =
        abstract member processOld : oldYIL: YIL.Program -> unit
        abstract member processNew : newYIL: YIL.Program -> unit
        abstract member processJoint : jointYIL: YIL.Program -> unit
        abstract member processCombine : combineYIL: YIL.Program -> unit
        
    /// Sample output-writing interface for diffing two files
    type DefaultTypecartOutput(outFolder: string) =
        let mkdir (f: string) = IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(f)) |> ignore  
        let write(fileName: string, p: YIL.Program) =
            let f = IO.Path.Combine(outFolder, fileName)
            mkdir f
            let s = YIL.printer().prog(p, YIL.Context(p))
            IO.File.WriteAllText(f, s)

        interface TypecartOutput with
            member this.processOld(oldYIL: YIL.Program) = write("old.dfy", oldYIL)
            member this.processNew(newYIL: YIL.Program) = write("new.dfy", newYIL)
            member this.processJoint(jointYIL: YIL.Program) = write("joint.dfy", jointYIL)
            member this.processCombine(combineYIL: YIL.Program) = write("combine.dfy", combineYIL)
    

    // replace "." with "\." and "/" with "\/" to make specifying regex on filenames easier.
    let private makeRegex (s: string) = s.Replace(".", "\.").Replace("/", "\/") |> Regex

    
    /// A project defines the directory scope typecart operates on.
    /// Typecart will read in every file in the directory structure of a project,
    /// and produce a diff.
    type TypecartProject(project: Utils.SystemPathKind, ignorePatterns: Regex list) =
        

        // append .typecartignore file to ignorePatterns list
        let currDirIgnores =
                // try to find .typecartignore file in current directory
                match project with
                | Utils.D dir ->
                    let objs = dir.GetFiles() |> List.ofSeq
                    match List.tryFind (fun (x: FileInfo) -> x.Name.Equals(".typecartignore")) objs with
                    | Some ignoreFile ->
                        ignoreFile.OpenText().ReadToEnd().Split(Environment.NewLine)
                        |> List.ofSeq
                        |> List.map makeRegex
                    | None -> []
                | _ -> []
        
        let isFilenameIgnored (fileName: string) =
            List.fold (fun (ignored: bool) (pattern: Regex) -> (pattern.IsMatch fileName) || ignored) false (ignorePatterns @ currDirIgnores)
        
        // constructor that helps read in ignore patterns from file
        new(project: Utils.SystemPathKind, ignorePatternsFile: string option) =
            let ign =
                match ignorePatternsFile with
                | Some ignoreFile ->
                    File.ReadLines(ignoreFile)
                    |> List.ofSeq
                    |> List.map makeRegex (* every line is turned into a regular expression *)
                | None -> []
            TypecartProject(project, ign)
            
        // when project is just a file
        new(f: FileInfo, ignorePatterns: Regex list) = TypecartProject(Utils.F f, ignorePatterns)
        new(f: FileInfo, ignorePatternsFile: string option) = TypecartProject(Utils.F f, ignorePatternsFile)
        // when project is an entire directory structure
        new(d: DirectoryInfo, ignorePatterns: Regex list) = TypecartProject(Utils.D d, ignorePatterns)
        new(d: DirectoryInfo, ignorePatternsFile: string option) = TypecartProject(Utils.D d, ignorePatternsFile)
        // generic entrypoint when we don't know whether path is a file or directory
        new(path: string, ignorePatterns: string option) = TypecartProject(Utils.parseSystemPath(path), ignorePatterns)
                
        // list of subdirectories of currenct project. Empty when project is just a file.
        member this.subDirectories =
            match project with
            | Utils.D currDir ->
                currDir.GetDirectories()
                |> List.ofSeq
                |> List.map (fun d -> TypecartProject(d, ignorePatterns))
            | Utils.F _ -> []
        
        // list of files of currenct project. singleton list when project is just a file.
        member this.files =
            match project with
            | Utils.D currDir ->
                currDir.GetFiles()
                |> List.ofSeq
                // filter out non-dafny files
                |> List.filter
                       (fun fd ->
                            match fd.Name with
                            | Utils.Suffix ".dfy" _ -> true
                            | _ -> false)
                // filter out filenames to be ignored
                // we filter based on the fully qualified path of the filename, just like the
                // behavior of .gitignore.
                |> List.filter (fun fd -> not (isFilenameIgnored fd.FullName))
            | Utils.F file -> [ file ]

        // flattens all the files
        member this.collect() =
            this.files @ List.collect (fun (subProject: TypecartProject) -> subProject.collect()) this.subDirectories
            
        // turn the directory into a single Dafny AST
        member this.toDafnyAST(projectName: string, reporter)  =
            Utils.parseASTs (this.collect()) projectName reporter
        
        // turn dafny AST representation of directory into a YIL AST
        member this.toYILProgram(projectName: string, reporter) =
            this.toDafnyAST(projectName, reporter) |> DafnyToYIL.program
    
    /// API entry    
    type Typecart(oldYIL: YIL.Program, newYIL: YIL.Program, logger: (string -> unit) option) =
        
        // helper utility function to get names in joint
        let jointNames joint = List.map (fun (p:YIL.Path) -> p.name) joint

        /// Pipelines for transforming old, new, joint, combine
        let processOld(joint: YIL.Path list): Traverser.Transform list = [
                Analysis.mkFilter(fun s -> not (List.contains s (jointNames joint)))
                Analysis.PrefixNotFoundImportsWithJoint()
                Analysis.PrefixTopDecls("Old")
                Analysis.ImportJointInOldNew()
                Analysis.AnalyzeModuleImports()
                Analysis.DeduplicateImportsIncludes()
                Analysis.CreateEmptyModuleIfNoneExists("Old")]
        let processNew(joint: YIL.Path list): Traverser.Transform list = [
                Analysis.mkFilter(fun s -> not (List.contains s (jointNames joint)))
                Analysis.PrefixNotFoundImportsWithJoint()
                Analysis.PrefixTopDecls("New")
                Analysis.ImportJointInOldNew()
                Analysis.AnalyzeModuleImports()
                Analysis.DeduplicateImportsIncludes()
                Analysis.CreateEmptyModuleIfNoneExists("New")]
        let processJoint(joint: YIL.Path list): Traverser.Transform list = [
                Analysis.mkFilter(fun s -> List.contains s (jointNames joint))
                Analysis.PrefixTopDecls("Joint")
                Analysis.AnalyzeModuleImports()
                Analysis.DeduplicateImportsIncludes()
                Analysis.CreateEmptyModuleIfNoneExists("Joint")]
        let processCombine: Traverser.Transform list = [
                Analysis.mkFilter(fun _ -> true)
                Analysis.PrefixTopDecls("Combine")
                Analysis.ImportInCombine()
                Analysis.AnalyzeModuleImports()
                Analysis.DeduplicateImportsIncludes()]
        
        // constructor without a logger instance
        new(oldYIL: YIL.Program, newYIL: YIL.Program) = Typecart(oldYIL, newYIL, None)
        // constructor with a logger instance
        new(oldYIL: YIL.Program, newYIL: YIL.Program, logger: string -> unit) = Typecart(oldYIL, newYIL, Some logger)
        
        member this.logger s =
            match logger with
            | None -> ()
            | Some logger -> logger s
        
        // run typecart on oldYIL and newYIL and call outputWriter to get output
        member this.go(outputWriter: TypecartOutput) =
            // tests the transformation code
            Traverser.test(oldYIL)
            
            // diff the programs
            this.logger "***** diffing the two programs"
            let diff = Differ.prog (oldYIL, newYIL)
            let diffS = (Diff.Printer()).prog diff
            this.logger diffS
            
            // generate translation
            this.logger "***** generating compatibility code"
            let combine,joint = Translation.prog(oldYIL, diff)
            
            // write output files
            this.logger "***** writing output files"
            
            let mk arg = arg |> Analysis.Pipeline
            
            (processJoint joint |> mk).apply(oldYIL) |> outputWriter.processJoint
            (processOld joint |> mk).apply(oldYIL) |> outputWriter.processOld
            (processNew joint |> mk).apply(newYIL) |> outputWriter.processNew
            (mk processCombine).apply(combine) |> outputWriter.processCombine

    let typecart(oldYIL: YIL.Program, newYIL: YIL.Program, logger: string -> unit) =
        Typecart(oldYIL, newYIL, logger)