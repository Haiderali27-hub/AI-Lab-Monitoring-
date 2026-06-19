import urllib.request
import os

def download_image(url, dest_path):
    try:
        # Stitch download links might block default python user-agents, so let's set a browser header
        req = urllib.request.Request(
            url, 
            headers={'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'}
        )
        with urllib.request.urlopen(req) as response:
            with open(dest_path, 'wb') as out_file:
                out_file.write(response.read())
        print(f"Successfully downloaded: {os.path.basename(dest_path)}")
    except Exception as e:
        print(f"Error downloading {os.path.basename(dest_path)}: {e}")

def main():
    dest_dir = r"c:\Users\DELL\Desktop\AI_Labmonitoring\important docs\fyp_doc_3chatp\Figures"
    
    # 1. Teacher Dashboard Overview
    url_dash = "https://lh3.googleusercontent.com/aida/AP1WRLtqELzK_DEPLdRL4HzS_H_Osf2WQmmfjuv3exDFQ5jK5Yl14hZUj4LkR2O9-LuvNKqMVg5EJHXX_-DsEhQ47IA6mxI94lblhrPjKargvKpZ730-k5nhcoxGh8P8_93E0VpWVrFBXD07jv9qc3_EdJp-XOij-QewW3z6unbnYps0W_kIjTU4-E3SURdBEZXtXazpLldPfFWk3FGmBkibDvxe738nyRAjHks53N0oqKt7_3IoFMT-hvSASKE"
    download_image(url_dash, os.path.join(dest_dir, "ui_teacher_dashboard.png"))
    
    # 2. Live Monitoring
    url_monitor = "https://lh3.googleusercontent.com/aida/AP1WRLucOWh8c5ru1gZVMiKeVbU1tqUemFPBRy-FptM2vOAaX9-P9WcEZewLLCZQFRbFe4tFC-E_ExBHWNDLB5TMAz5oIHgdBirtzZdD6xwWB7iyXeIXQ4Y1R89UZeiEBKyDsQZ_TtruNE4paLDC7kpOUEntmlWS5KTw7jilqO4v3TKmd174Ssuqu7nmPNA1GSuRFtEO_ORGQW3D6IPsENA5CYTLLmMoq9L-xzxl_HdARo189ewKfRxqWF_diaQ"
    download_image(url_monitor, os.path.join(dest_dir, "ui_teacher_live_monitoring.png"))
    
    # 3. AI Grading Panel
    url_grading = "https://lh3.googleusercontent.com/aida/AP1WRLsuJurLd_22asKgdFhW7_UhsnnXGAg4yQLMdDSQ5JLbC5ctrnWbsrCJLwd-Gfp9epZEBZHHZVjoyWNBjaVdsm_d-EUOCCUmBqtgXlDswe6nyGA_w71gzjJzI4FyN-TOTayuDbTg_wjUSOU5TjeZC3kSXRti_XkYj2Y-f7kDEBiOJnY9ZmaLVteWrY8P-5T7jUeSMiG4lrFpj3i_7uasV9jXWfnCOdQ0dfmL5P5AavxIiZo97xx7f4jnirU"
    download_image(url_grading, os.path.join(dest_dir, "ui_teacher_ai_grading.png"))

if __name__ == "__main__":
    main()
