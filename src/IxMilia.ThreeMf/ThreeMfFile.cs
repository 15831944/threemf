﻿// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace IxMilia.ThreeMf
{
    public class ThreeMfFile
    {
        private const string ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
        private const string RelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
        private const string ModelRelationshipType = "http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel";
        private const string ExtensionAttributeName = "Extension";
        private const string ContentTypeAttributeName = "ContentType";
        private const string RelsExtension = "rels";
        private const string ModelExtension = "model";
        private const string RelsContentType = "application/vnd.openxmlformats-package.relationships+xml";
        private const string ModelContentType = "application/vnd.ms-package.3dmanufacturing-3dmodel+xml";
        private const string ContentTypesPath = "[Content_Types].xml";
        private const string DefaultModelEntryPath = "/3D/3dmodel.model";
        private const string RelsEntryPath = "_rels/.rels";
        private const string DefaultRelationshipId = "rel0";
        private const string TargetAttributeName = "Target";
        private const string IdAttributeName = "Id";
        private const string TypeAttributeName = "Type";

        private static XName TypesName = XName.Get("Types", ContentTypesNamespace);
        private static XName DefaultName = XName.Get("Default", ContentTypesNamespace);
        private static XName RelationshipsName = XName.Get("Relationships", RelationshipNamespace);
        private static XName RelationshipName = XName.Get("Relationship", RelationshipNamespace);

        private static XmlWriterSettings WriterSettings = new XmlWriterSettings()
        {
            Encoding = Encoding.UTF8,
            Indent = true,
            IndentChars = "  "
        };

        public IList<ThreeMfModel> Models { get; } = new List<ThreeMfModel>();

        public void Save(Stream stream)
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var contentTypes = new XElement(TypesName,
                    GetDefaultContentType(RelsExtension, RelsContentType),
                    GetDefaultContentType(ModelExtension, ModelContentType));
                WriteXmlToArchive(archive, contentTypes, ContentTypesPath);

                var rels = new XElement(RelationshipsName,
                    new XElement(RelationshipName,
                        new XAttribute(TargetAttributeName, DefaultModelEntryPath),
                        new XAttribute(IdAttributeName, DefaultRelationshipId),
                        new XAttribute(TypeAttributeName, ModelRelationshipType)));
                WriteXmlToArchive(archive, rels, RelsEntryPath);

                // TODO: handle more than one model
                var model = Models.SingleOrDefault() ?? new ThreeMfModel();
                var modelXml = model.ToXElement();
                var modelArchivePath = DefaultModelEntryPath.Substring(1); // trim the leading slash for ZipArchive
                WriteXmlToArchive(archive, modelXml, modelArchivePath);
            }
        }

        private static XElement GetDefaultContentType(string extension, string contentType)
        {
            return new XElement(DefaultName,
                new XAttribute(ExtensionAttributeName, extension),
                new XAttribute(ContentTypeAttributeName, contentType));
        }

        private static void WriteXmlToArchive(ZipArchive archive, XElement xml, string path)
        {
            var entry = archive.CreateEntry(path);
            using (var stream = entry.Open())
            using (var writer = XmlWriter.Create(stream, WriterSettings))
            {
                var document = new XDocument(xml);
                document.WriteTo(writer);
            }
        }

        public static ThreeMfFile Load(Stream stream)
        {
            using (var archive = new ZipArchive(stream))
            {
                var modelFilePath = GetModelFilePath(archive);
                var modelEntry = archive.GetEntry(modelFilePath);
                if (modelEntry == null)
                {
                    throw new ThreeMfPackageException("Package does not contain a model.");
                }

                using (var modelStream = modelEntry.Open())
                {
                    var document = XDocument.Load(modelStream);
                    var model = ThreeMfModel.LoadXml(document.Root);
                    var file = new ThreeMfFile();
                    file.Models.Add(model); // assume one model for now
                    return file;
                }
            }
        }

        private static string GetModelFilePath(ZipArchive archive)
        {
            var relsEntry = archive.GetEntry(RelsEntryPath);
            if (relsEntry == null)
            {
                throw new ThreeMfPackageException("Invalid package: missing relationship file.");
            }

            using (var relsStream = relsEntry.Open())
            {
                var document = XDocument.Load(relsStream);
                var firstRelationship = document.Root.Elements(RelationshipName).FirstOrDefault(e => e.Attribute(TypeAttributeName)?.Value == ModelRelationshipType);
                if (firstRelationship == null)
                {
                    throw new ThreeMfPackageException("Package does not contain a root 3MF relation.");
                }

                var target = firstRelationship.Attribute(TargetAttributeName)?.Value;
                if (target == null)
                {
                    throw new ThreeMfPackageException("Relationship target not specified.");
                }

                if (target.StartsWith("/"))
                {
                    // ZipArchive doesn't like the leading slash
                    target = target.Substring(1);
                }

                return target;
            }
        }
    }
}
