// ReSharper disable InconsistentNaming
using Raven.Client.Documents.Indexes;

namespace Prius.Data.RavenDB.Indexes;

public sealed class Packages_Packages_ByIdAndVersion : AbstractIndexCreationTask
{
    public override IndexDefinition CreateIndexDefinition() => new()
    {
        Name = "Packages/Packages/ByIdAndVersion",
        Maps = { 
            """
            map('Packages', function (doc) {
                var tfms = {};
                if (doc.Dependencies) {
                    for (var tfm in doc.Dependencies) {
                        tfms[tfm] = true;
                    }
                }
                if (doc.SupportedFrameworks) {
                    for (var tfm in doc.SupportedFrameworks) {
                        if (tfm !== 'Order') {
                            tfms[tfm] = true;
                        }
                    }
                }
                if (doc.Assets && doc.Assets.lib) {
                    for (var tfm in doc.Assets.lib) {
                        tfms[tfm] = true;
                    }
                }
                var tfmList = Object.keys(tfms);
                if (tfmList.length === 0) {
                    tfmList.push('any');
                }
                return {
                    Id: doc.Info ? doc.Info.id : null,
                    Version: doc.Info ? doc.Info.version : null,
                    Tfms: tfmList
                };
            })
            """ 
        },
        Fields = new()
        {
            { "Id", new IndexFieldOptions { Indexing = FieldIndexing.Exact, Storage = FieldStorage.Yes } },
            { "Version", new IndexFieldOptions { Indexing = FieldIndexing.Exact, Storage = FieldStorage.Yes } },
            { "Tfms", new IndexFieldOptions { Indexing = FieldIndexing.Exact, Storage = FieldStorage.Yes } }
        }
    };
}

public sealed class Packages_Assets_ByHash : AbstractIndexCreationTask
{
    public override IndexDefinition CreateIndexDefinition() => new()
    {
        Name = "Packages/Assets/ByHash",
        Maps = { 
            """
            map('Packages', function (doc) {
                var results = [];
                if (!doc.Assets || !doc.Assets.lib) return results;
                
                for (var tfmKey in doc.Assets.lib) {
                    var tfmFolder = doc.Assets.lib[tfmKey];
                    if (!tfmFolder) continue;
                    
                    for (var fileKey in tfmFolder) {
                        var fileNode = tfmFolder[fileKey];
                        
                        if (fileNode && (fileNode.Hash || fileNode.hash)) {
                            results.push({ 
                                Hash: fileNode.Hash || fileNode.hash
                            });
                        }
                    }
                }
                return results;
            })
            """ 
        },
        Fields = new()
        {
            { "Hash", new IndexFieldOptions { Indexing = FieldIndexing.Exact, Storage = FieldStorage.Yes } }
        }
    };
}
