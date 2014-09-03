﻿namespace FSharp.Data.Sql.Common
    
open System

module internal Utilities = 
    
    open System.IO

    type TempFile(path:string) =
         member val Path = path with get
         interface IDisposable with 
            member this.Dispose() = File.Delete(path)

    let tempFile(extension : string) =
        let filename =
            let tempF = Path.GetTempFileName()
            let tempF' = Path.ChangeExtension(tempF, extension)
            if tempF <> tempF' then
                File.Delete tempF
            tempF'
        new TempFile(filename)
    
    let resolveTuplePropertyName (name:string) (tupleIndex:string ResizeArray) =
        // eg "Item1" -> tupleIndex.[0]
        tupleIndex.[(int <| name.Remove(0, 4)) - 1]

module internal ConfigHelpers = 
    
    open System
    open System.IO
    open System.Configuration

    let tryGetConnectionString root (connectionStringName:string) (connectionString:string) =
        if String.IsNullOrWhiteSpace(connectionString)
        then
            let configFilePath = 
                [
                    Path.Combine(root, "app.config")
                    Path.Combine(root, "web.config")
                ] |> List.find File.Exists
            use tempFile = Utilities.tempFile "config"
            File.Copy(configFilePath, tempFile.Path)
            let fileMap = new ExeConfigurationFileMap(ExeConfigFilename = tempFile.Path)
            let config = ConfigurationManager.OpenMappedExeConfiguration(fileMap, ConfigurationUserLevel.None)
            match config.ConnectionStrings.ConnectionStrings.[connectionStringName] with
            | null -> None
            | a -> Some(a.ConnectionString)
        else Some(connectionString)

module internal SchemaProjections = 
    
    let buildTableName (tableName:string) = tableName.Substring(0,tableName.LastIndexOf("]")+1).ToUpper()

    let buildFieldName (fieldName:string) = fieldName.ToUpper()

    let buildSprocName (sprocName:string) = sprocName.ToUpper()

module internal Sql =
    
    open System
    open System.Data

    let dataReaderToArray (reader:IDataReader) = 
        [| 
            while reader.Read() do
               yield [|      
                    for i = 0 to reader.FieldCount - 1 do 
                        match reader.GetValue(i) with
                        | null | :? DBNull ->  yield (reader.GetName(i),null)
                        | value -> yield (reader.GetName(i),value)
               |]
        |]

    let dbUnbox<'a> (v:obj) : 'a = 
        if Convert.IsDBNull(v) then Unchecked.defaultof<'a> else unbox v
    
    let dbUnboxWithDefault<'a> def (v:obj) : 'a = 
        if Convert.IsDBNull(v) then def else unbox v

    let connect (con:IDbConnection) f =
        if con.State <> ConnectionState.Open then con.Open()
        let result = f con
        con.Close(); result

    let executeSql createCommand sql (con:IDbConnection) =        
        use com : IDbCommand = createCommand sql con   
        com.ExecuteReader()    

    let executeSqlAsDataTable createCommand sql con = 
        executeSql createCommand sql con
        |> (fun r -> 
             let dt = new DataTable(); 
             dt.Load(r); 
             dt)