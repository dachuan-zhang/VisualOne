﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using PPT = Microsoft.Office.Interop.PowerPoint;
using MSO = Microsoft.Office.Core;
using System.Windows.Forms;
using System.Collections;

namespace VisualOne
{
    public class CatalogClass
    {
        public List<BluePrint> m_bluePrints = new List<BluePrint>();
        Dictionary<Guid, TempSeenKeptRecord> m_performanceValues = new Dictionary<Guid, TempSeenKeptRecord>();


        // private string m_sourceRoot = @"\\ppt-svc\Features\AutoLayout\DSNR\Blueprints\Active";
        // private string m_outputRoot = @"\\ppt-svc\Features\AutoLayout\CatalogTool\data\04-01\";
#pragma warning disable IDE0044 // Add readonly modifier
        private string m_sourceRoot = @"c:\sample\";
        private string m_outputRoot = @"c:\flat.png\";
#pragma warning restore IDE0044 // Add readonly modifier

        public string SourceRoot { get { return m_sourceRoot; } }
        public string RenderedRoot { get { return m_outputRoot; } }

        public CatalogClass(string sourceRoot, string outputRoot)
        {
            if (sourceRoot != null)
            {
                this.m_sourceRoot = sourceRoot;
                if (!sourceRoot.EndsWith("\\"))
                    this.m_sourceRoot += "\\";
            }
            if ( outputRoot != null)
            {
                this.m_outputRoot = outputRoot;
                if (!outputRoot.EndsWith("\\"))
                    this.m_outputRoot += "\\";
            }
        }

        private void ReadPerformanceSummary()
        {
            StreamReader summaryReader = File.OpenText(RenderedRoot + "7day.txt");
            while (!summaryReader.EndOfStream)
            {
                string bpLine = summaryReader.ReadLine();
                string[] segments = bpLine.Split('\t');
                if (segments.Count() != 3)
                    continue;
                string guidStr = segments[0];
                string seen = segments[1];
                string kept = segments[2];
                try
                {
                    UInt32 seenCount = UInt32.Parse(seen);
                    UInt32 keptCount = UInt32.Parse(kept);
                    if (keptCount > seenCount)
                        throw new ArgumentOutOfRangeException();
                    TempSeenKeptRecord record = new TempSeenKeptRecord
                    {
                        seen = seenCount,
                        kept = keptCount
                    };
                    Guid guid = Guid.Parse(guidStr);
                    m_performanceValues.Add(guid, record);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }

        private void ChangeGuid(PPT.Slide newSlide)
         {
            foreach (PPT.Shape sp in newSlide.NotesPage.Shapes)
            {
                if (sp.HasTextFrame == MSO.MsoTriState.msoTrue)
                {
                    string notesText = sp.TextFrame2.TextRange.Text;
                    if (notesText.StartsWith("ID="))
                    {
                        notesText = notesText.Replace('\r', '\n');      // Clean up
                        string[] properties = notesText.Split('\n');
                        string bluePrintGuid = properties[0].Substring(3);
                        string newGuid = System.Guid.NewGuid().ToString();
                        notesText.Replace(properties[0], "ID=" + newGuid);
                        sp.TextFrame2.TextRange.Replace(properties[0], "ID=" + newGuid);
                        return;
                    }
                }
            }
        }

        public void OpenBP(BluePrint bp)
        {
            var pptApp = new PPT.Application();
            PPT.Presentation pres = pptApp.Presentations.Open(bp.flatPath, MSO.MsoTriState.msoTrue);

            // Find the exact blueprint slide
            foreach (PPT.Slide sld in pres.Slides)
            {
                foreach (PPT.Shape sp in sld.NotesPage.Shapes)
                {
                    if (sp.HasTextFrame == MSO.MsoTriState.msoTrue)
                    {
                        string notesText = sp.TextFrame2.TextRange.Text;
                        if (notesText.StartsWith("ID="))
                        {
                            notesText = notesText.Replace('\r', '\n');      // Clean up
                            string[] properties = notesText.Split('\n');
                            string bluePrintGuid = properties[0].Substring(3);
                            if(bluePrintGuid == bp.guid.ToString())
                            {
                                sld.Select();
                            }
                        }
                    }
                }
            }
        }

        internal void DuplicateBPToActivePresentation(BluePrint bp)
        {
            var pptApp = new PPT.Application();
            if (pptApp.Presentations.Count < 1)
                return;     // TODO: Alert need a target to apply
            PPT.Presentation targetPres = pptApp.Presentations[1];
            PPT.Presentation sourcePres = pptApp.Presentations.Open(bp.flatPath,
                                                                    MSO.MsoTriState.msoTrue,
                                                                    MSO.MsoTriState.msoFalse,
                                                                    MSO.MsoTriState.msoFalse);
            float sourceSlideHeight = sourcePres.PageSetup.SlideHeight;
            float sourceSlideWidth = sourcePres.PageSetup.SlideWidth;
            float targetSlideCenterY = targetPres.PageSetup.SlideHeight / 2;
            float targetSlideCenterX = targetPres.PageSetup.SlideWidth / 2;

            float vResizeRatio = (sourceSlideWidth / sourceSlideHeight)/ (targetSlideCenterX / targetSlideCenterY);

            // Find the exact blueprint slide
            foreach (PPT.Slide sld in sourcePres.Slides)
            {
                // Paste over to target presentation
                sld.Select();
                sld.Copy();
                var newSlides = targetPres.Slides.Paste();
                PPT.Slide newSlide = newSlides[1];
                foreach (PPT.Shape shape in newSlide.Shapes)
                {
                    float centerX = shape.Left + shape.Width / 2;
                    float centerY = shape.Top + shape.Height / 2;
                    float newY = (centerY - targetSlideCenterY) * vResizeRatio + targetSlideCenterY;
                    float newHeight = shape.Height;
                    if (shape.Type == MSO.MsoShapeType.msoAutoShape && shape.AutoShapeType == MSO.MsoAutoShapeType.msoShapeRectangle)
                        newHeight = shape.Height * vResizeRatio;
                    else if (shape.Type == MSO.MsoShapeType.msoTextBox)
                        newHeight = shape.Height * vResizeRatio;
                    else if (shape.Type == MSO.MsoShapeType.msoGroup)
                        newHeight = shape.Height * vResizeRatio;
//                    else if (shape.Type == MSO.MsoShapeType.msoPicture)
//                        newHeight = shape.Height * vResizeRatio;
                    else if (shape.Type == MSO.MsoShapeType.msoPlaceholder)
                        newHeight = shape.Height * vResizeRatio;
                    shape.Top = newY - newHeight / 2;
                    shape.Height = newHeight;
                }
                // Change GUID of the target slide
                ChangeGuid(newSlide);
            }
            sourcePres.Close();
        }

        public void RasterizeOriginals()
        {
            var pptApp = new PPT.Application();
            string directory = m_sourceRoot;
            var pptxFiles = Directory.EnumerateFiles(directory, "*.pptx", SearchOption.AllDirectories);
            List<string> failedFiles = new List<string>();
            List<string> successFiles = new List<string>();
            List<string> failedFinalFiles = new List<string>();

            foreach (string currentFile in pptxFiles)
            {
                try
                {
                    RasterizeOneOriginal(pptApp, currentFile, directory, RenderedRoot);
                    successFiles.Add(currentFile);
                }
                catch (Exception e)
                {
                    failedFiles.Add(currentFile);
                    Console.WriteLine(e.Message);
                    if ((uint)e.HResult == 0x0800706ba)
                        pptApp = new PPT.Application();
                }
            }

            foreach (string currentFile in failedFiles)
            {
                try
                {
                    RasterizeOneOriginal(pptApp, currentFile, directory, RenderedRoot);
                    successFiles.Add(currentFile);
                }
                catch (Exception e)
                {
                    failedFinalFiles.Add(currentFile);
                    Console.WriteLine(e.Message);
                }
            }
        }

        private void AllGuidsWithPngs(string[] pngFiles, HashSet<Guid> guids)
        {
            var renderedRootIndex = RenderedRoot.Length;
            foreach(var path in pngFiles)
            {
                if (!path.StartsWith(RenderedRoot))
                    continue;
                string file = path.Substring(renderedRootIndex);
                var dotIndex = file.IndexOf('.');
                Guid guid;
                if (dotIndex > 0 && Guid.TryParse(file.Substring(0, dotIndex), out guid))
                    guids.Add(guid);
            }
        }

        public void ReadFlatCatalog()
        {
            string[] pngFiles = Directory.GetFiles(RenderedRoot, "*.png", SearchOption.TopDirectoryOnly);
            HashSet<Guid> guidsWithPngs = new HashSet<Guid>();
            AllGuidsWithPngs(pngFiles, guidsWithPngs);
            StreamReader catalogReader = File.OpenText(SourceRoot + "Catalog.txt");
            ReadPerformanceSummary();
            int guidStringLength = Guid.Empty.ToString().Length;
            while (!catalogReader.EndOfStream)
            {
                string bpLine = catalogReader.ReadLine();
                if (bpLine == string.Empty)
                    continue;
                int firstSpaceIndex = bpLine.IndexOf(' ');
                if(firstSpaceIndex != guidStringLength)
                {
                    Console.WriteLine(bpLine);
                    continue;
                }

                Guid bluePrintGuid;
                if(!Guid.TryParse( bpLine.Substring(0, firstSpaceIndex), out bluePrintGuid ))
                    continue;
                string original = bpLine.Substring(firstSpaceIndex + 1);
                string[] segments = original.Split('\\');
                string layout = segments[0];
                string type = segments[1];
                string crop = segments[2];
                string aspect = segments[3];
                string path = segments[4];
                string pngPath = RenderedRoot + bluePrintGuid + ".png";
                string flatPptxPath = SourceRoot + bluePrintGuid + ".pptx";
                if (guidsWithPngs.Contains(bluePrintGuid))
                {
                    BluePrint bp = new BluePrint
                    {
                        source = path,
                        flatPath = flatPptxPath,
                        originalPath = original,
                        layout = layout,
                        type = type,
                        aspectRaio = aspect,
                        cropNonCrop = crop,
                        guid = bluePrintGuid,
                        pngPath = pngPath,
                    };
                    if (m_performanceValues.ContainsKey(bluePrintGuid))
                    {
                        bp.kept = m_performanceValues[bluePrintGuid].kept;
                        bp.seen = m_performanceValues[bluePrintGuid].seen;
                        if (bp.seen > 0)
                            bp.keptRate = (double)bp.kept * 800 / bp.seen;
                    }
                    m_bluePrints.Add(bp);
                }
                else
                {
                    Console.WriteLine(pngPath);
                }
            }
            catalogReader.Close();
        }

        private void RasterizeOneOriginal(PPT.Application app, string currentFile, string directory, string outputRoot)
        {
            string fileName = currentFile.Substring(directory.Length + 1);
            string[] segments = fileName.Split('\\');
            string layout = segments[0];
            string type = segments[1];
            string crop = segments[2];
            string aspect = segments[3];
            string path = segments[4];
            if (path.StartsWith("~"))
                return;

            PPT.Presentation pres = app.Presentations.Open(currentFile, MSO.MsoTriState.msoTrue);
            string outputPath = outputRoot + layout + "_" + type + "_" + crop + "_" + aspect + "_" + path + "\\";
            Directory.CreateDirectory(outputPath);
            try
            {
                PPT.Slide sld = pres.Slides[1];
                var newSlide = pres.Slides.AddSlide(1, sld.CustomLayout);
                newSlide.Export(outputPath + "original" + ".png", "png");
            }
            catch (Exception e)
            {
                throw e;
            }
            finally
            {
                pres.Close();
            }
        }
    }
}
