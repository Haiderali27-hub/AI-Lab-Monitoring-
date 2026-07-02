import pypdf

def main():
    pdf_path = r"c:\Users\DELL\Desktop\AI_Labmonitoring\important docs\reference_fyp_dpc.pdf"
    reader = pypdf.PdfReader(pdf_path)
    content = ""
    # Extract pages 77 to 90 (0-indexed: 76 to 89)
    for i in range(76, 90):
        content += f"--- Page {i+1} ---\n"
        content += reader.pages[i].extract_text() + "\n"
    with open("reference_chapter_5.txt", "w", encoding="utf-8") as f:
        f.write(content)
    print("Extracted Chapter 5 successfully!")

if __name__ == "__main__":
    main()
