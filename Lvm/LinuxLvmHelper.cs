/* Copyright (C) 2023-2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using DiskAccessLibrary;
using System;
using System.Collections.Generic;
using System.Text;
using Utilities;

namespace VmdkZeroFree.Lvm
{
    public class LinuxLvmHelper
    {
        private const byte LinuxRaidPartitionType = 0xFD;
        private const uint LvmSuperblockSignature = 0xA92B4EFC; // MD_SB_MAGIC
        private const ulong LvmPhysicalVolumeLabelSignature = 0x454E4F4C4542414C; // "LABELONE"
        private const ulong Lvm2MetadataSignatureLow = 0x5B7820324D564C20; // LVM2 x[5A%r0N*>
        private const ulong Lvm2MetadataSignatureHigh = 0x3E2A4E3072254135;

        public static List<DiskExtent> GetUnderlyingVolumes(Disk disk, PartitionTableEntry partitionTableEntry)
        {
            long partitionStartLBA = partitionTableEntry.FirstSectorLBA;
            long partitionSizeLBA = partitionTableEntry.SectorCountLBA;
            if (partitionTableEntry.SectorCountLBA > 0 && partitionTableEntry.PartitionType == LinuxRaidPartitionType)
            {
                byte[] possibleLvmSuperblockBytes = disk.ReadSector(partitionStartLBA + 8);
                uint possibleLvmSuperblockSignature = LittleEndianConverter.ToUInt32(possibleLvmSuperblockBytes, 0);
                uint version = LittleEndianConverter.ToUInt32(possibleLvmSuperblockBytes, 0x04);
                if (possibleLvmSuperblockSignature == LvmSuperblockSignature && version == 1)
                {
                    return GetUnderlyingLvmVolumes(disk, partitionStartLBA, possibleLvmSuperblockBytes);
                }
            }

            return new List<DiskExtent>()
            {
                new DiskExtent(disk, partitionStartLBA, partitionSizeLBA * disk.BytesPerSector)
            };
        }

        private static List<DiskExtent> GetUnderlyingLvmVolumes(Disk disk, long partitionStartLBA, byte[] lvmSuperblockBytes)
        {
            ulong dataOffset = LittleEndianConverter.ToUInt64(lvmSuperblockBytes, 0x80);
            ulong dataSizeLBA = LittleEndianConverter.ToUInt64(lvmSuperblockBytes, 0x88);

            byte[] possibleLvmPhysicalVolumeHeaderBytes = disk.ReadSector(partitionStartLBA + (long)dataOffset + 1);
            ulong possibleLvmPhysicalVolumeLabelSignature = LittleEndianConverter.ToUInt64(possibleLvmPhysicalVolumeHeaderBytes, 0);

            if (possibleLvmPhysicalVolumeLabelSignature == LvmPhysicalVolumeLabelSignature)
            {
                // https://wiki.syslinux.org/wiki/index.php?title=Development/LVM_support
                ulong actualDataOffsetInBytes = LittleEndianConverter.ToUInt64(possibleLvmPhysicalVolumeHeaderBytes, 0x48);
                long actualDataOffsetLBA = (long)actualDataOffsetInBytes / disk.BytesPerSector;
                ulong actualDataSizeInBytes = LittleEndianConverter.ToUInt64(possibleLvmPhysicalVolumeHeaderBytes, 0x40); // In bytes
                long actualDataSizeInSectors =  (long)actualDataSizeInBytes / disk.BytesPerSector;
                ulong metadataStartOffsetInBytes = LittleEndianConverter.ToUInt64(possibleLvmPhysicalVolumeHeaderBytes, 0x68);
                long metadataStartOffsetLBA = (long)metadataStartOffsetInBytes / disk.BytesPerSector;
                ulong metadataSize = LittleEndianConverter.ToUInt64(possibleLvmPhysicalVolumeHeaderBytes, 0x70);
                int metadataSizeInSectors = (int)metadataSize / disk.BytesPerSector;
                byte[] metadataBytes = disk.ReadSectors(partitionStartLBA + (long)dataOffset + metadataStartOffsetLBA, metadataSizeInSectors);
                ulong possibleMetadataSignatureLow = LittleEndianConverter.ToUInt64(metadataBytes, 0x04);
                ulong possibleMetadataSignatureHigh = LittleEndianConverter.ToUInt64(metadataBytes, 0x0C);
                if (possibleMetadataSignatureLow == Lvm2MetadataSignatureLow && possibleMetadataSignatureHigh == Lvm2MetadataSignatureHigh)
                {
                    ulong metadataValueOffset = LittleEndianConverter.ToUInt64(metadataBytes, 0x28);
                    ulong metadataValueLength = LittleEndianConverter.ToUInt64(metadataBytes, 0x30);
                    string metadata = Encoding.ASCII.GetString(metadataBytes, (int)metadataValueOffset, (int)metadataValueLength);
                    return GetUnderlyingLvmVolumes(disk, partitionStartLBA, (long)dataOffset + actualDataOffsetLBA, actualDataSizeInSectors, metadata);
                }
            }

            return new List<DiskExtent>()
            {
                new DiskExtent(disk, partitionStartLBA + (long)dataOffset, (long)dataSizeLBA * disk.BytesPerSector)
            };
        }

        private static List<DiskExtent> GetUnderlyingLvmVolumes(Disk disk, long partitionStartLBA, long dataOffset, long dataSize, string metadata)
        {
            List<DiskExtent> result = new List<DiskExtent>();
            string format = LvmMetadataParser.GetValue(metadata, "format");
            if (format.Trim('\"') == "lvm2")
            {
                int extentSize = Convert.ToInt32(LvmMetadataParser.GetValue(metadata, "extent_size"));
                string physicalVolumesMetadata = LvmMetadataParser.GetMetadataObject(metadata, "physical_volumes");
                List<string> physicalVolumeNames = LvmMetadataParser.GetChildObjects(physicalVolumesMetadata);
                if (physicalVolumeNames.Count == 1)
                {
                    string physicalVolumeMetadata = LvmMetadataParser.GetMetadataObject(metadata, physicalVolumeNames[0]);
                    long devSize = Convert.ToInt64(LvmMetadataParser.GetValue(physicalVolumeMetadata, "dev_size"));
                    if (dataSize == devSize)
                    {
                        long peStart = Convert.ToInt64(LvmMetadataParser.GetValue(physicalVolumeMetadata, "pe_start"));
                        long peCount = Convert.ToInt64(LvmMetadataParser.GetValue(physicalVolumeMetadata, "pe_count"));

                        string logicalVolumesMetadata = LvmMetadataParser.GetMetadataObject(metadata, "logical_volumes");
                        List<string> logicalVolumes = LvmMetadataParser.GetChildObjects(logicalVolumesMetadata);
                        foreach (string logicalVolumeName in logicalVolumes)
                        {
                            string logicalVolumeMetadata = LvmMetadataParser.GetMetadataObject(logicalVolumesMetadata, logicalVolumeName);
                            string segmentCount = LvmMetadataParser.GetValue(logicalVolumeMetadata, "segment_count");
                            if (segmentCount == "1")
                            {
                                string segmentMetadata = LvmMetadataParser.GetMetadataObject(logicalVolumeMetadata, "segment1");
                                if (segmentMetadata != null)
                                {
                                    long startExtent = Convert.ToInt64(LvmMetadataParser.GetValue(segmentMetadata, "start_extent"));
                                    long extentCount = Convert.ToInt64(LvmMetadataParser.GetValue(segmentMetadata, "extent_count"));
                                    string segmentType = LvmMetadataParser.GetValue(segmentMetadata, "type");
                                    if (segmentType.Trim('\"') == "striped")
                                    {
                                        string[] stripesMetadata = LvmMetadataParser.GetArrayValue(segmentMetadata, "stripes");
                                        long startOffset = Convert.ToInt64(stripesMetadata[1]);

                                        result.Add(new DiskExtent(disk, partitionStartLBA + dataOffset + startOffset * extentSize, extentCount * extentSize * disk.BytesPerSector));
                                    }
                                }

                            }
                        }
                    }
                }
            }

            return result;
        }
    }
}
