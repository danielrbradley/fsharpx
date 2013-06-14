﻿module FSharpx.TypeProviders.ExcelProvider

open System.IO
open System
open Samples.FSharp.ProvidedTypes
open Microsoft.FSharp.Core.CompilerServices
open FSharpx.TypeProviders.Helper
open System.Collections.Generic
open System.Data
open System
open Excel

let parseExcelAddress cellAddress =

    let convertToBase radix digits =
        let digitValue i digit = float digit * Math.Pow(float radix, float i)

        digits
        |> List.rev
        |> List.mapi digitValue
        |> Seq.sum
        |> int
        
    let charToDigit char = ((int)(Char.ToUpper(char))) - 64

    let column = 
        cellAddress 
        |> Seq.filter Char.IsLetter 
        |> Seq.map charToDigit
        |> Seq.toList
        |> convertToBase 26

    let row =
        cellAddress
        |> Seq.filter Char.IsNumber
        |> Seq.map (string >> Int32.Parse)
        |> Seq.toList
        |> convertToBase 10

    (row - 1), (column - 1)

let internal getCells (workbook : DataSet) sheetOrRangeName headerRowIndex =
    let worksheets = workbook.Tables
    
    //if sheetOrRangeName refers to a worksheet get the header row from the specified worksheet
    if worksheets.Contains(sheetOrRangeName)  then
        let sheet = worksheets.[sheetOrRangeName]

        //remove unecessary leading rows
        if headerRowIndex > 0 then do
            for row in 0 .. headerRowIndex do
                let removeRow = sheet.Rows.[headerRowIndex]
                sheet.Rows.Remove(removeRow);
        sheet
    else
        let sheet = worksheets.[0]
        let topLeft = 0, 0
        let bottomRight = 
        if sheetOrRangeName.Contains(":") then
            let addresses = sheetOrRangeName.Split(':');
            let topLeft = parseExcelAddress addresses.[0]
            let bottomRight = parseExcelAddress addresses.[1]

            
        else
            
          
        else
        failwith (sprintf "Sheet or range %A was not found" sheetOrRangeName)

// Simple type wrapping Excel data
type  ExcelFileInternal(filename, sheetorrangename, headerRow : int) =

    let data  = 
        use stream = File.OpenRead(filename)
        let excelReader = 
            if filename.EndsWith(".xlsx") then ExcelReaderFactory.CreateOpenXmlReader(stream)
            else ExcelReaderFactory.CreateBinaryReader(stream)

        let workbook = excelReader.AsDataSet()
        let data = getCells workbook sheetorrangename headerRow

        let res = seq { for irow  in 2 .. data.Rows.Count do
                        yield seq { for jcol in 1 .. data.Columns.Count do
                                        yield data.Rows.[irow].[jcol] }
                                |> Seq.toArray }
                    |> Seq.toArray
        res

    member __.Data = data

type internal ReflectiveBuilder = 
   static member Cast<'a> (args:obj) =
      args :?> 'a
   static member BuildTypedCast lType (args: obj) = 
         typeof<ReflectiveBuilder>
            .GetMethod("Cast")
            .MakeGenericMethod([|lType|])
            .Invoke(null, [|args|])

type internal GlobalSingleton private () =
   static let mutable instance = Dictionary<_, _>()
   static member Instance = instance

let internal memoize f =
      //let cache = Dictionary<_, _>()
      fun x ->
         if (GlobalSingleton.Instance).ContainsKey(x) then (GlobalSingleton.Instance).[x]
         else let res = f x
              (GlobalSingleton.Instance).[x] <- res
              res


let internal typExcel(cfg:TypeProviderConfig) =
   // Create the main provided type
   let excTy = ProvidedTypeDefinition(System.Reflection.Assembly.GetExecutingAssembly(), rootNamespace, "ExcelFile", Some(typeof<obj>))

   let defaultHeaderRow = 1

   // Parameterize the type by the file to use as a template
   let filename = ProvidedStaticParameter("filename", typeof<string>)
   let sheetorrangename = ProvidedStaticParameter("sheetname", typeof<string>, "Sheet1")
   let forcestring = ProvidedStaticParameter("forcestring", typeof<bool>, false)
   let headerRow = ProvidedStaticParameter("headerrow", typedefof<int>, defaultHeaderRow)

   let staticParams = [ filename
                        sheetorrangename   
                        forcestring
                        headerRow ]

   do excTy.DefineStaticParameters(staticParams, fun tyName paramValues ->
      let (filename, sheetorrangename, forcestring, headerRow) = 
            match paramValues with
            | [| :? string  as filename;   :? string as sheetorrangename;  :? bool as forcestring;  :? int as headerRow|] -> (filename, sheetorrangename, forcestring, headerRow)
            | [| :? string  as filename;   :? string as sheetorrangename;  :? bool as forcestring |] -> (filename, sheetorrangename, forcestring, defaultHeaderRow)
            | [| :? string  as filename;   :? bool as forcestring |] -> (filename, "Sheet1", forcestring, defaultHeaderRow)
            | [| :? string  as filename|] -> (filename, "Sheet1", false, defaultHeaderRow)
            | _ -> ("no file specified to type provider", "",  true, defaultHeaderRow)

         // [| :? string as filename ,  :? bool  as forcestring |]
         // resolve the filename relative to the resolution folder
      let resolvedFilename = Path.Combine(cfg.ResolutionFolder, filename)

      let ProvidedTypeDefinitionExcelCall (filename, sheetorrangename,  forcestring, headerRow)  =
         
         let xlRangeInput = getRange xlWorkBookInput sheetorrangename headerRow

         let lines = (seq { for row in xlRangeInput.Rows do yield row } |> Seq.cache)
         let headerLine =  (Seq.head   lines):?> Excel.Range
         // define a provided type for each row, erasing to a float[]
         let rowTy = ProvidedTypeDefinition("Row", Some(typeof<obj[]>))

         let oFirstdataLine  =  
            match (Seq.length lines) with
               | 1 -> None
               | _  -> Some( lines |> Seq.skip 1 |> Seq.head :?> Excel.Range)            

         // add one property per Excel field
         for i in 0 .. (headerLine.Columns.Count - 1 ) do

            let header = (headerLine.Cells.Item(1,i+1) :?> Excel.Range).Value2
            if header <> null then do
                let headerText = header.ToString()
            
                let valueType, gettercode  = 
                   if  forcestring || oFirstdataLine = None then
                      typeof<string>, (fun [row] -> <@@ ((%%row:obj[]).[i]) |> string  @@>)
                   else
                      let firstdataLine = oFirstdataLine.Value
                      if xlApp.WorksheetFunction.IsText(firstdataLine.Cells.Item(1,i+1)) then
                         typeof<string>, (fun [row] -> <@@ ((%%row:obj[]).[i]) |> string  @@>)
                      elif xlApp.WorksheetFunction.IsNumber(firstdataLine.Cells.Item(1,i+1)) then
                         typeof<float> , (fun [row] -> <@@ ((%%row:obj[]).[i]) :?> float  @@>)
                      else
                         typeof<string>, (fun [row] -> <@@ ((%%row:obj[]).[i]) |> string  @@>)

                //TODO : test w different types
                let prop = ProvidedProperty(headerText, valueType, GetterCode = gettercode)
                // Add metadata defining the property's location in the referenced file
                prop.AddDefinitionLocation(1, i, filename)
                rowTy.AddMember(prop)

         // define the provided type, erasing to excelFile
         let ty = ProvidedTypeDefinition(System.Reflection.Assembly.GetExecutingAssembly(), rootNamespace, tyName, Some(typeof<ExcelFileInternal>))

         // add a parameterless constructor which loads the file that was used to define the schema
         ty.AddMember(ProvidedConstructor([], InvokeCode = fun [] -> <@@ ExcelFileInternal(resolvedFilename, sheetorrangename, headerRow) @@>))
         // add a constructor taking the filename to load
         ty.AddMember(ProvidedConstructor([ProvidedParameter("filename", typeof<string>)], InvokeCode = fun [filename] -> <@@  ExcelFileInternal(%%filename, sheetorrangename, headerRow) @@>))
         // add a new, more strongly typed Data property (which uses the existing property at runtime)
         ty.AddMember(ProvidedProperty("Data", typedefof<seq<_>>.MakeGenericType(rowTy), GetterCode = fun [excFile] -> <@@ (%%excFile:ExcelFileInternal).Data @@>))
         // add the row type as a nested type
         ty.AddMember(rowTy)
         ty

      (memoize ProvidedTypeDefinitionExcelCall)(filename, sheetorrangename, forcestring, headerRow)
      )

   // add the type to the namespace
   excTy

[<TypeProvider>]
type public ExcelProvider(cfg:TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces()

    do this.AddNamespace(rootNamespace,[typExcel cfg])

[<TypeProviderAssembly>]
do ()