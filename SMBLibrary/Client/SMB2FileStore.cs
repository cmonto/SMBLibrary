/* Copyright (C) 2017-2019 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Collections.Generic;
using SMBLibrary.SMB2;
using Utilities;

namespace SMBLibrary.Client
{
    public class SMB2FileStore : ISMBFileStore
    {
        private SMB2Client m_client;
        private uint m_treeID;

        public SMB2FileStore(SMB2Client client, uint treeID)
        {
            m_client = client;
            m_treeID = treeID;
        }

        public NTStatus CreateFile(out object handle, out FileStatus fileStatus, string path, AccessMask desiredAccess, FileAttributes fileAttributes, ShareAccess shareAccess, CreateDisposition createDisposition, CreateOptions createOptions, SecurityContext securityContext)
        {
            handle = null;
            fileStatus = FileStatus.FILE_DOES_NOT_EXIST;
            CreateRequest request = new CreateRequest();
            request.Name = path;
            request.DesiredAccess = desiredAccess;
            request.FileAttributes = fileAttributes;
            request.ShareAccess = shareAccess;
            request.CreateDisposition = createDisposition;
            request.CreateOptions = createOptions;
            request.ImpersonationLevel = ImpersonationLevel.Impersonation;
            ulong messageId = TrySendCommand(request);

            SMB2Command response = m_client.WaitForCommand(SMB2CommandName.Create, messageId);
            if (response != null)
            {
                if (response.Header.Status == NTStatus.STATUS_SUCCESS && response is CreateResponse)
                {
                    CreateResponse createResponse = ((CreateResponse)response);
                    handle = createResponse.FileId;
                    fileStatus = ToFileStatus(createResponse.CreateAction);
                }
                return response.Header.Status;
            }

            return NTStatus.STATUS_INVALID_SMB;
        }

        public NTStatus CloseFile(object handle)
        {
            CloseRequest request = new CloseRequest();
            request.FileId = (FileID)handle;
            ulong messageId = TrySendCommand(request);
            SMB2Command response = m_client.WaitForCommand(SMB2CommandName.Close, messageId);
            if (response != null)
            {
                return response.Header.Status;
            }

            return NTStatus.STATUS_INVALID_SMB;
        }

        public NTStatus ReadFile(out byte[] data, object handle, long offset, int maxCount)
        {
            data = null;
            ReadRequest request = new ReadRequest();
            request.FileId = (FileID)handle;
            request.Offset = (ulong)offset;
            request.ReadLength = (uint)maxCount;

            ulong messageId = TrySendCommand(request);
            SMB2Command response = m_client.WaitForCommand(SMB2CommandName.Read, messageId);
            if (response != null)
            {
                if (response.Header.Status == NTStatus.STATUS_SUCCESS && response is ReadResponse)
                {
                    data = ((ReadResponse)response).Data;
                }
                return response.Header.Status;
            }

            return NTStatus.STATUS_INVALID_SMB;
        }

        public NTStatus WriteFile(out int numberOfBytesWritten, object handle, long offset, byte[] data)
        {
            numberOfBytesWritten = 0;
            WriteRequest request = new WriteRequest();
            request.FileId = (FileID)handle;
            request.Offset = (ulong)offset;
            request.Data = data;

            ulong messageId = TrySendCommand(request);
            SMB2Command response = m_client.WaitForCommand(SMB2CommandName.Write, messageId);
            if (response != null)
            {
                if (response.Header.Status == NTStatus.STATUS_SUCCESS && response is WriteResponse)
                {
                    numberOfBytesWritten = (int)((WriteResponse)response).Count;
                }
                return response.Header.Status;
            }

            return NTStatus.STATUS_INVALID_SMB;
        }

        public NTStatus FlushFileBuffers(object handle)
        {
            throw new NotImplementedException();
        }

        public NTStatus LockFile(object handle, long byteOffset, long length, bool exclusiveLock)
        {
            throw new NotImplementedException();
        }

        public NTStatus UnlockFile(object handle, long byteOffset, long length)
        {
            throw new NotImplementedException();
        }

        public NTStatus QueryDirectory(out List<QueryDirectoryFileInformation> result, object handle, string fileName, FileInformationClass informationClass)
        {
            result = new List<QueryDirectoryFileInformation>();
            QueryDirectoryRequest request = new QueryDirectoryRequest();
            request.FileInformationClass = informationClass;
            request.Reopen = true;
            request.FileId = (FileID)handle;
            request.OutputBufferLength = m_client.MaxTransactSize;
            request.FileName = fileName;

            ulong messageId = TrySendCommand(request);
            SMB2Command response = m_client.WaitForCommand(SMB2CommandName.QueryDirectory, messageId);
            if (response != null)
            {
                while (response.Header.Status == NTStatus.STATUS_SUCCESS && response is QueryDirectoryResponse)
                {
                    List<QueryDirectoryFileInformation> page = ((QueryDirectoryResponse)response).GetFileInformationList(informationClass);
                    result.AddRange(page);
                    request.Reopen = false;
                    messageId = TrySendCommand(request);
                    response = m_client.WaitForCommand(SMB2CommandName.QueryDirectory, messageId);
                }
                return response.Header.Status;
            }

            return NTStatus.STATUS_INVALID_SMB;
        }

        public NTStatus GetFileInformation(out FileInformation result, object handle, FileInformationClass informationClass)
        {
            result = null;
            QueryInfoRequest request = new QueryInfoRequest();
            request.InfoType = InfoType.File;
            request.FileInformationClass = informationClass;
            request.OutputBufferLength = 4096;
            request.FileId = (FileID)handle;

            ulong messageId = TrySendCommand(request);
            SMB2Command response = m_client.WaitForCommand(SMB2CommandName.QueryInfo, messageId);
            if (response != null)
            {
                if (response.Header.Status == NTStatus.STATUS_SUCCESS && response is QueryInfoResponse)
                {
                    result = ((QueryInfoResponse)response).GetFileInformation(informationClass);
                }
                return response.Header.Status;
            }

            return NTStatus.STATUS_INVALID_SMB;
        }

        public NTStatus SetFileInformation(object handle, FileInformation information)
        {
            SetInfoRequest request = new SetInfoRequest();
            request.InfoType = InfoType.File;
            request.FileInformationClass = information.FileInformationClass;
            request.FileId = (FileID)handle;
            request.SetFileInformation(information);

            ulong messageId = TrySendCommand(request);
            SMB2Command response = m_client.WaitForCommand(SMB2CommandName.SetInfo, messageId);
            if (response != null)
            {
                return response.Header.Status;
            }

            return NTStatus.STATUS_INVALID_SMB;
        }

        public NTStatus GetFileSystemInformation(out FileSystemInformation result, FileSystemInformationClass informationClass)
        {
            result = null;
            object fileHandle;
            FileStatus fileStatus;
            NTStatus status = CreateFile(out fileHandle, out fileStatus, String.Empty, (AccessMask)DirectoryAccessMask.FILE_LIST_DIRECTORY | (AccessMask)DirectoryAccessMask.FILE_READ_ATTRIBUTES | AccessMask.SYNCHRONIZE, 0, ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete, CreateDisposition.FILE_OPEN, CreateOptions.FILE_SYNCHRONOUS_IO_NONALERT | CreateOptions.FILE_DIRECTORY_FILE, null);
            if (status != NTStatus.STATUS_SUCCESS)
            {
                return status;
            }

            status = GetFileSystemInformation(out result, fileHandle, informationClass);
            CloseFile(fileHandle);
            return status;
        }

        public NTStatus GetFileSystemInformation(out FileSystemInformation result, object handle, FileSystemInformationClass informationClass)
        {
            result = null;
            QueryInfoRequest request = new QueryInfoRequest();
            request.InfoType = InfoType.FileSystem;
            request.FileSystemInformationClass = informationClass;
            request.OutputBufferLength = 4096;
            request.FileId = (FileID)handle;

            ulong messageId = TrySendCommand(request);
            SMB2Command response = m_client.WaitForCommand(SMB2CommandName.QueryInfo, messageId);
            if (response != null)
            {
                if (response.Header.Status == NTStatus.STATUS_SUCCESS && response is QueryInfoResponse)
                {
                    result = ((QueryInfoResponse)response).GetFileSystemInformation(informationClass);
                }
                return response.Header.Status;
            }

            return NTStatus.STATUS_INVALID_SMB;
        }

        public NTStatus SetFileSystemInformation(FileSystemInformation information)
        {
            throw new NotImplementedException();
        }

        public NTStatus GetSecurityInformation(out SecurityDescriptor result, object handle, SecurityInformation securityInformation)
        {
            result = null;
            QueryInfoRequest request = new QueryInfoRequest();
            request.InfoType = InfoType.Security;
            request.SecurityInformation = securityInformation;
            request.OutputBufferLength = 4096;
            request.FileId = (FileID)handle;

            ulong messageId = TrySendCommand(request);
            SMB2Command response = m_client.WaitForCommand(SMB2CommandName.QueryInfo, messageId);
            if (response != null)
            {
                if (response.Header.Status == NTStatus.STATUS_SUCCESS && response is QueryInfoResponse)
                {
                    result = ((QueryInfoResponse)response).GetSecurityInformation();
                }
                return response.Header.Status;
            }

            return NTStatus.STATUS_INVALID_SMB;
        }

        public NTStatus SetSecurityInformation(object handle, SecurityInformation securityInformation, SecurityDescriptor securityDescriptor)
        {
            return NTStatus.STATUS_NOT_SUPPORTED;
        }

        public NTStatus NotifyChange(out object ioRequest, object handle, NotifyChangeFilter completionFilter, bool watchTree, int outputBufferSize, OnNotifyChangeCompleted onNotifyChangeCompleted, object context)
        {
            throw new NotImplementedException();
        }

        public NTStatus Cancel(object ioRequest)
        {
            throw new NotImplementedException();
        }

        public NTStatus DeviceIOControl(object handle, uint ctlCode, byte[] input, out byte[] output, int maxOutputLength)
        {
            output = null;
            IOCtlRequest request = new IOCtlRequest();
            request.CtlCode = ctlCode;
            request.IsFSCtl = true;
            request.FileId = (FileID)handle;
            request.Input = input;
            request.MaxOutputResponse = (uint)maxOutputLength;
            ulong messageId = TrySendCommand(request);
            SMB2Command response = m_client.WaitForCommand(SMB2CommandName.IOCtl, messageId);
            if (response != null)
            {
                if ((response.Header.Status == NTStatus.STATUS_SUCCESS || response.Header.Status == NTStatus.STATUS_BUFFER_OVERFLOW) && response is IOCtlResponse)
                {
                    output = ((IOCtlResponse)response).Output;
                }
                return response.Header.Status;
            }

            return NTStatus.STATUS_INVALID_SMB;
        }

        public NTStatus Disconnect()
        {
            TreeDisconnectRequest request = new TreeDisconnectRequest();
            ulong messageId = TrySendCommand(request);
            SMB2Command response = m_client.WaitForCommand(SMB2CommandName.TreeDisconnect, messageId);
            if (response != null)
            {
                return response.Header.Status;
            }

            return NTStatus.STATUS_INVALID_SMB;
        }

        private ulong TrySendCommand(SMB2Command request)
        {
            request.Header.TreeID = m_treeID;
            return m_client.TrySendCommand(request);
        }

        public uint MaxReadSize
        {
            get
            {
                return m_client.MaxReadSize;
            }
        }

        public uint MaxWriteSize
        {
            get
            {
                return m_client.MaxWriteSize;
            }
        }

        private static FileStatus ToFileStatus(CreateAction createAction)
        {
            switch (createAction)
            {
                case CreateAction.FILE_SUPERSEDED:
                    return FileStatus.FILE_SUPERSEDED;
                case CreateAction.FILE_OPENED:
                    return FileStatus.FILE_OPENED;
                case CreateAction.FILE_CREATED:
                    return FileStatus.FILE_CREATED;
                case CreateAction.FILE_OVERWRITTEN:
                    return FileStatus.FILE_OVERWRITTEN;
                default:
                    return FileStatus.FILE_OPENED;
            }
        }
    }
}
