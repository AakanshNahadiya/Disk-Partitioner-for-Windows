using DiskPartitioner.Services;
using Xunit;

namespace DiskPartitioner.Tests
{
    public class DiskpartScriptTests
    {
        [Fact]
        public void BuildCleanScript_TargetsExactDisk()
        {
            string script = DiskpartExecutionService.BuildCleanScript(2);
            Assert.Contains("select disk 2", script);
            Assert.Contains("clean", script);
        }

        [Fact]
        public void BuildConvertScript_ConvertsToGptOrMbr()
        {
            string gpt = DiskpartExecutionService.BuildConvertScript(1, "gpt");
            Assert.Contains("select disk 1", gpt);
            Assert.Contains("convert gpt", gpt);

            string mbr = DiskpartExecutionService.BuildConvertScript(1, "MBR");
            Assert.Contains("select disk 1", mbr);
            Assert.Contains("convert mbr", mbr);
        }

        [Fact]
        public void BuildCreatePartitionScript_FullParameters()
        {
            string script = DiskpartExecutionService.BuildCreatePartitionScript(1, 20480, "E:", "ntfs", "Games");
            Assert.Contains("select disk 1", script);
            Assert.Contains("create partition primary size=20480", script);
            Assert.Contains("assign letter=E", script);
            Assert.Contains("format fs=ntfs quick label=\"Games\"", script);
        }

        [Fact]
        public void BuildCreatePartitionScript_MaxSizeWithoutFormatting()
        {
            string script = DiskpartExecutionService.BuildCreatePartitionScript(2, 0, null, null, null);
            Assert.Contains("select disk 2", script);
            Assert.Contains("create partition primary", script);
            Assert.DoesNotContain("size=", script);
            Assert.DoesNotContain("assign letter", script);
            Assert.DoesNotContain("format", script);
        }

        [Fact]
        public void BuildCreatePartitionScript_FullFormatWithoutQuick()
        {
            string script = DiskpartExecutionService.BuildCreatePartitionScript(1, 20480, "E:", "ntfs", "Games", quick: false);
            Assert.Contains("select disk 1", script);
            Assert.Contains("create partition primary size=20480", script);
            Assert.Contains("assign letter=E", script);
            Assert.Contains("format fs=ntfs label=\"Games\"", script);
            Assert.DoesNotContain("quick", script);
        }

        [Fact]
        public void BuildFormatScript_QuickAndFullFormat()
        {
            string quick = DiskpartExecutionService.BuildFormatScript(1, 2, "exfat", "Backup", quick: true);
            Assert.Contains("select disk 1", quick);
            Assert.Contains("select partition 2", quick);
            Assert.Contains("format fs=exfat quick label=\"Backup\"", quick);

            string full = DiskpartExecutionService.BuildFormatScript(1, 2, "ntfs", "", quick: false);
            Assert.Contains("format fs=ntfs", full);
            Assert.DoesNotContain("quick", full);
        }

        [Fact]
        public void BuildDeletePartitionScript_StandardAndOverride()
        {
            string std = DiskpartExecutionService.BuildDeletePartitionScript(1, 3, overrideFlag: false);
            Assert.Equal("select disk 1\nselect partition 3\ndelete partition\n", std);

            string ovr = DiskpartExecutionService.BuildDeletePartitionScript(1, 4, overrideFlag: true);
            Assert.Equal("select disk 1\nselect partition 4\ndelete partition override\n", ovr);
        }

        [Fact]
        public void BuildExtendAndShrinkScripts()
        {
            string ext = DiskpartExecutionService.BuildExtendScript(1, 2, 5120);
            Assert.Contains("extend size=5120", ext);

            string extAll = DiskpartExecutionService.BuildExtendScript(1, 2, 0);
            Assert.Contains("extend\n", extAll);

            string shrink = DiskpartExecutionService.BuildShrinkScript(1, 2, 10240);
            Assert.Contains("shrink desired=10240", shrink);

            string shrinkVol = DiskpartExecutionService.BuildShrinkScript(0, 3, 5120, "C:");
            Assert.Contains("select volume C", shrinkVol);
            Assert.Contains("shrink desired=5120", shrinkVol);

            string extVol = DiskpartExecutionService.BuildExtendScript(0, 3, 5120, "D:");
            Assert.Contains("select volume D", extVol);
            Assert.Contains("extend size=5120", extVol);
        }

        [Fact]
        public void BuildAssignAndRemoveLetterScripts()
        {
            string assign = DiskpartExecutionService.BuildAssignLetterScript(1, 2, 'f');
            Assert.Contains("assign letter=F", assign);

            string remove = DiskpartExecutionService.BuildRemoveLetterScript(1, 2, 'f');
            Assert.Contains("remove letter=F", remove);
        }
    }
}
