import pypdf

def main():
    pdf_path = r"c:\Users\DELL\Desktop\AI_Labmonitoring\important docs\fyp first submitted doc .pdf"
    reader = pypdf.PdfReader(pdf_path)
    print(f"Total pages: {len(reader.pages)}")
    text = reader.pages[0].extract_text()
    print("Page 1 Text snippet:")
    print(text[:300])

if __name__ == "__main__":
    main()
