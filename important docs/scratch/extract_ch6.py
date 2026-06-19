import pypdf

def main():
    pdf_path = r"c:\Users\DELL\Desktop\AI_Labmonitoring\important docs\reference_fyp_dpc.pdf"
    reader = pypdf.PdfReader(pdf_path)
    content = ""
    # Extract pages 91 to 101 (0-indexed: 90 to 100)
    for i in range(90, 101):
        content += f"--- Page {i+1} ---\n"
        content += reader.pages[i].extract_text() + "\n"
    with open("reference_chapter_6.txt", "w", encoding="utf-8") as f:
        f.write(content)
    print("Extracted Chapter 6 successfully!")

if __name__ == "__main__":
    main()
