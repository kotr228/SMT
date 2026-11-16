# Лабораторна робота №5. Варіант 15
# Задана матриця E(7*7). Переписати всі парні елементи матриці в одномірний масив X
from random import randint

m = n = 7  # розмірність матриці

# формування матриці випадковими числами та її вивід
E = [[0] * n for i in range(m)]
print("Матриця E:")
for i in range(m):
    for j in range(n):
        E[i][j] = randint(-20, 20)
        print("{:4d}".format(E[i][j]), end=" ")
    print()

# переписуємо парні елементи в масив X
X = []
for i in range(m):
    for j in range(n):
        if E[i][j] % 2 == 0:
            X.append(E[i][j])

# вивід масиву X по 7 елементів у рядку
print("Масив X (парні елементи матриці):")
for k in range(len(X)):
    print("{:4d}".format(X[k]), end=" ")
    if (k + 1) % 7 == 0:
        print()
if len(X) % 7 != 0:
    print()
print("Кількість елементів у масиві X =", len(X))
