import urllib.request
import os

def download_image(url, dest_path):
    try:
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
    
    # 1. Student Login Screen
    url_login = "https://lh3.googleusercontent.com/aida/AP1WRLuA-v1XdSrn_o98byy6lgnaLxMwgv9Bowx2tVic86zaWXNaxVmNJdVqGYYctIASJKY4Yyy_DdPCSh7P7g8Kfw8o8VVLryUJVbb__tEIhafi7dOvxg7NZnVvbk7S9HPTOdhBlY1oR36ybk_UNoXAv3saVbKkfmdmEzuU9Irr_QvhIysf1Ni9MdrY1frj0GGWvLV5PjKXUvJ2IP2rHBmB4qBVySAWzO2ELjvBBbcjvhaektWZJOrVGMUDMQ"
    download_image(url_login, os.path.join(dest_dir, "ui_student_login.png"))
    
    # 2. Student Workspace Screen (Exam Environment)
    url_workspace = "https://lh3.googleusercontent.com/aida/AP1WRLvTcvxHISFeu92-7l67PqiW-Cd4TbnpMAR3jdDNTBkHpr1EsYyife4gOQQmBNeDslg5AVvHlZL8i5XZO0pH8P8T-jnsrk04NhRgUXLRy8V7NNtXQ2bMOJfjnyQGheKEwcBGU6869izQo2GF9S9znU4tdriM5xLd__DlJ9kvowGDINuMNbO8wZLeDqj5HiR7ajosMMgN-E4wmnlwf2ZP7afgTG56Ibl2Mkz_hP_2EEc0pWfU_9PhbiwuLw"
    download_image(url_workspace, os.path.join(dest_dir, "ui_student_workspace.png"))

if __name__ == "__main__":
    main()
