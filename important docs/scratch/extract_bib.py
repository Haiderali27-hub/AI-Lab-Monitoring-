import pypdf

def main():
    pdf_path = r"c:\Users\DELL\Desktop\AI_Labmonitoring\important docs\reference_fyp_dpc.pdf"
    reader = pypdf.PdfReader(pdf_path)
    print(f"Total pages: {len(reader.pages)}")
    
    content = ""
    # Extract the last few pages (from page 120 to end)
    for i in range(119, len(reader.pages)):
        content += f"--- Page {i+1} ---\n"
        content += reader.pages[i].extract_text() + "\n"
    with open("reference_bibliography.txt", "w", encoding="utf-8") as f:
        f.write(content)
    print("Extracted bibliography successfully!")

if __name__ == "__main__":
    main()
