# SourceFilesGenerator

The purpose of this repo is to have a source files generator that takes the DbContext and generates the respective models files, 
as well as the related Blazor CRUD pages.

Ideally we can turn this into a Visual Studio extension, similar to EF Core Power Tools.

## Videos
* Generate Shared Models From EF Core https://fairplaytube.pticostarica.com/Public/Videos/Details/387e6f493a
* Automatically Generate Blazor CRUD https://fairplaytube.pticostarica.com/Public/Videos/Details/19efe1579f
* Blazor CRUD Generator Tool https://fairplaytube.pticostarica.com/Public/Videos/Details/85af7ab017

## Limitations
* Will only generated Data Annotations on models when source entities are using Data Annotations approach
* Does not generate UI controls for navigation properties/lookups
* Creates a controller per entity
