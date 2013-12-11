using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Connectivity.WebServices;
using Autodesk.DataManagement.Client.Framework.Vault.Currency.Connections;
using Autodesk.DataManagement.Client.Framework.Vault.Currency.Entities;
using Autodesk.DataManagement.Client.Framework.Vault.Currency.FileSystem;
using Autodesk.DataManagement.Client.Framework.Vault.Forms.Settings;
using Autodesk.DataManagement.Client.Framework.Vault.Models;
using Autodesk.DataManagement.Client.Framework.Vault.Results;
using Autodesk.DataManagement.Client.Framework.Vault.Settings;
using File = Autodesk.Connectivity.WebServices.File;

namespace UpdatingReferencesSample
{
	public class Program
	{
		private static void Main(string[] args)
		{
			var vaultConnection = GetVaultConnection();
			if (vaultConnection == null)
				return;

			Console.WriteLine("Please enter a full VaultPath (e.g. $/Design/Test.ipt):");
			var userEntry = Console.ReadLine();

			//Get the DocumentService
			DocumentService documentService = vaultConnection.WebServiceManager.DocumentService;

			//Get the File from Vault
			File fileFromVault = GetFileByFullPath(documentService, userEntry);

			//Download file to Workspace
			AcquireFilesResults downloadResults = DownloadFile(vaultConnection, fileFromVault);

			//Update References from File
			UpdateRefsInLocalFile(documentService, fileFromVault, downloadResults);

			Console.WriteLine("\n\nPress <Enter> to terminate process:");
			Console.ReadLine();
		}


		public static Connection GetVaultConnection()
		{
			Console.WriteLine("Connecting to Vault...");
			Autodesk.DataManagement.Client.Framework.Library.Initialize(false);
			var loginSettings = new LoginSettings { AutoLoginMode = LoginSettings.AutoLoginModeValues.RestoreAndExecute };
			var connection = Autodesk.DataManagement.Client.Framework.Vault.Forms.Library.Login(loginSettings);
			if (connection == null)
				Console.WriteLine("ERROR: Connecting to Vault failed!");
			return connection;
		}

		public static File GetFileByFullPath(DocumentService documentService, string fullPath)
		{
			try
			{
				if (String.IsNullOrEmpty(fullPath))
					return null;

				var fileName = Path.GetFileName(fullPath);
				var folderPath = fullPath.Replace(fileName, "");
				var folder = documentService.GetFolderByPath(folderPath);
				if (folder == null)
					return null;

				var filesInFolder = documentService.GetLatestFilesByFolderId(folder.Id, true);
				return filesInFolder.FirstOrDefault(f => f.Name == fileName);
			}
			catch (Exception e)
			{
				Console.WriteLine("Error occured when getting File from Vault, please check the FilePath and FileName: {0}", e);
				return null;
			}
		}


		public static AcquireFilesResults DownloadFile(Connection vaultConnection, File file)
		{
			var fileIteration = new FileIteration(vaultConnection, file);
			var settings = new AcquireFilesSettings(vaultConnection);
			settings.CheckoutComment = "File is CheckedOut By UpdatingReferences";
			settings.LocalPath = null;
			settings.OptionsResolution.ForceOverwrite = true;
			settings.OptionsRelationshipGathering.FileRelationshipSettings.IncludeChildren = true;
			settings.OptionsRelationshipGathering.FileRelationshipSettings.RecurseChildren = true;
			settings.OptionsRelationshipGathering.FileRelationshipSettings.IncludeLibraryContents = true;
			settings.OptionsRelationshipGathering.FileRelationshipSettings.VersionGatheringOption =
				Autodesk.DataManagement.Client.Framework.Vault.Currency.VersionGatheringOption.Latest;
			settings.DefaultAcquisitionOption = AcquireFilesSettings.AcquisitionOption.Download;
			settings.AddFileToAcquire(fileIteration, AcquireFilesSettings.AcquisitionOption.Download);

			var downloadedFiles = vaultConnection.FileManager.AcquireFiles(settings);
			return downloadedFiles;
		}

		public static void UpdateRefsInLocalFile(DocumentService documentService, File file, AcquireFilesResults results)
		{
			foreach (FileAcquisitionResult acquireFilesResult in results.FileResults)
			{
				var assocs = documentService.GetFileAssociationsByIds(new[] { acquireFilesResult.File.EntityIterationId },
																	  FileAssociationTypeEnum.None, false,
																	  FileAssociationTypeEnum.Dependency, false, false, false);
				if (assocs.First().FileAssocs == null)
					continue;
				var fileAssocs = assocs.First().FileAssocs.Where(fa => fa.ParFile.MasterId == acquireFilesResult.File.EntityMasterId);
				var refs = new List<FileReference>();
				foreach (FileAssoc fileAssoc in fileAssocs)
				{
					var fileCld = results.FileResults.FirstOrDefault(f => f.File.EntityMasterId == fileAssoc.CldFile.MasterId);
					if (fileCld == null)
						continue;
					var reference = new FileReference(fileAssoc.RefId, fileCld.LocalPath, fileAssoc.Source);
					refs.Add(reference);
				}

				var updateReferenceModel = new UpdateFileReferencesModel();
				updateReferenceModel.SetTargetFilePath(acquireFilesResult.File, acquireFilesResult.LocalPath);
				updateReferenceModel.UpdateRefsInLocalFile(acquireFilesResult.File, acquireFilesResult.LocalPath, refs);
			}
		}
	}
}