﻿//MIT, 2017, Zou Wei(github/zwcloud), WinterDev
using System.Collections.Generic;
using Typography.OpenFont;
using Typography.TextLayout;
using Typography.Contours;

namespace DrawingGL.Text
{
    /// <summary>
    /// text printer
    /// </summary>
    class TextPrinter : TextPrinterBase
    {
        //funcs:
        //1. layout glyph
        //2. measure glyph
        //3. generate glyph runs into textrun 
        GlyphTranslatorToPath pathTranslator;
        string currentFontFile;
        GlyphPathBuilder currentGlyphPathBuilder;

        //
        // for tess
        // 
        SimpleCurveFlattener _curveFlattener;
        TessTool _tessTool;

        Typeface _currentTypeface;

        //-------------
        struct ProcessedGlyph
        {
            public readonly float[] tessData;
            public readonly ushort tessNElements;
            public ProcessedGlyph(float[] tessData, ushort tessNElements)
            {
                this.tessData = tessData;
                this.tessNElements = tessNElements;
            }
        }
        GlyphMeshCollection<ProcessedGlyph> _glyphMeshCollection = new GlyphMeshCollection<ProcessedGlyph>();
        //-------------
        public TextPrinter()
        {
            FontSizeInPoints = 14;
            ScriptLang = ScriptLangs.Latin;

            //
            _curveFlattener = new SimpleCurveFlattener();

            _tessTool = new TessTool();
        }


        public override void DrawFromGlyphPlans(PxScaledGlyphPlanList glyphPlanList, int startAt, int len, float x, float y)
        {
            throw new System.NotImplementedException();
        }
        public override void DrawFromGlyphPlans(GlyphPlanSequence glyphPlanList, int startAt, int len, float x, float y)
        {
            throw new System.NotImplementedException();
        }
        public override GlyphLayout GlyphLayoutMan { get; } = new GlyphLayout();

        public override Typeface Typeface
        {
            get { return _currentTypeface; }
            set
            {
                _currentTypeface = value;
                GlyphLayoutMan.Typeface = value;
            }
        }
        public MeasuredStringBox Measure(char[] textBuffer, int startAt, int len)
        {
            return GlyphLayoutMan.LayoutAndMeasureString(
                textBuffer, startAt, len,
                this.FontSizeInPoints
                );
        }

        /// <summary>
        /// Font file path
        /// </summary>
        public string FontFilename
        {
            get { return currentFontFile; }
            set
            {
                if (currentFontFile != value)
                {
                    currentFontFile = value;

                    //TODO: review here
                    using (var stream = Utility.ReadFile(value))
                    {
                        var reader = new OpenFontReader();
                        Typeface = reader.Read(stream);
                    }

                    //2. glyph builder
                    currentGlyphPathBuilder = new GlyphPathBuilder(Typeface);
                    currentGlyphPathBuilder.UseTrueTypeInstructions = false; //reset
                    currentGlyphPathBuilder.UseVerticalHinting = false; //reset
                    switch (this.HintTechnique)
                    {
                        case HintTechnique.TrueTypeInstruction:
                            currentGlyphPathBuilder.UseTrueTypeInstructions = true;
                            break;
                        case HintTechnique.TrueTypeInstruction_VerticalOnly:
                            currentGlyphPathBuilder.UseTrueTypeInstructions = true;
                            currentGlyphPathBuilder.UseVerticalHinting = true;
                            break;
                        case HintTechnique.CustomAutoFit:
                            //custom agg autofit 
                            break;
                    }

                    //3. glyph translater
                    pathTranslator = new GlyphTranslatorToPath();

                    //4. Update GlyphLayout
                    GlyphLayoutMan.ScriptLang = this.ScriptLang;
                    GlyphLayoutMan.PositionTechnique = this.PositionTechnique;
                    GlyphLayoutMan.EnableLigature = this.EnableLigature;
                }
            }
        }



        /// <summary>
        /// generate glyph run into a given textRun
        /// </summary>
        /// <param name="outputTextRun"></param>
        /// <param name="charBuffer"></param>
        /// <param name="start"></param>
        /// <param name="len"></param>
        public void GenerateGlyphRuns(TextRun outputTextRun, char[] charBuffer, int start, int len)
        {
            // layout glyphs with selected layout technique
            float sizeInPoints = this.FontSizeInPoints;
            outputTextRun.typeface = this.Typeface;
            outputTextRun.sizeInPoints = sizeInPoints;

            //in this version we store original glyph into the mesh collection
            //and then we scale it later, so I just specific font size=0 (you can use any value)
            _glyphMeshCollection.SetCacheInfo(this.Typeface, 0, this.HintTechnique);


            GlyphLayoutMan.Typeface = this.Typeface;
            GlyphLayoutMan.Layout(charBuffer, start, len);

            float pxscale = this.Typeface.CalculateScaleToPixelFromPointSize(sizeInPoints);

            PxScaledGlyphPlanList userGlyphPlans = new PxScaledGlyphPlanList();
            GenerateGlyphPlan(charBuffer, 0, charBuffer.Length, userGlyphPlans, null);

            // render each glyph 
            int planCount = userGlyphPlans.Count;
            for (var i = 0; i < planCount; ++i)
            {

                pathTranslator.Reset();
                //----
                //glyph path 
                //---- 
                PxScaledGlyphPlan glyphPlan = userGlyphPlans[i];
                //
                //1. check if we have this glyph in cache?
                //if yes, not need to build it again 
                ProcessedGlyph processGlyph;
                float[] tessData = null;

                if (!_glyphMeshCollection.TryGetCacheGlyph(glyphPlan.glyphIndex, out processGlyph))
                {
                    //if not found the  create a new one and register it
                    var writablePath = new WritablePath();
                    pathTranslator.SetOutput(writablePath);
                    currentGlyphPathBuilder.BuildFromGlyphIndex(glyphPlan.glyphIndex, sizeInPoints);
                    currentGlyphPathBuilder.ReadShapes(pathTranslator);

                    //-------
                    //do tess  
                    int[] endContours;
                    float[] flattenPoints = _curveFlattener.Flatten(writablePath._points, out endContours);
                    int nTessElems;
                    tessData = _tessTool.TessPolygon(flattenPoints, endContours, out nTessElems);
                    //-------
                    processGlyph = new ProcessedGlyph(tessData, (ushort)nTessElems);
                    _glyphMeshCollection.RegisterCachedGlyph(glyphPlan.glyphIndex, processGlyph);
                }

                outputTextRun.AddGlyph(
                    new GlyphRun(glyphPlan,
                        processGlyph.tessData,
                        processGlyph.tessNElements));
            }
        }
        public override void DrawString(char[] textBuffer, int startAt, int len, float x, float y)
        {

        }
        public override void DrawCaret(float x, float y)
        {

        }
    }


}