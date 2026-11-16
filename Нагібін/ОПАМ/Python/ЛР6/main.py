# Лабораторна робота №6. Варіант 3
# Дано матриці G, H, T. Обчислити R = G*H - 2T
from random import randint


def show(name, M):
    """Вивід матриці у вигляді таблиці"""
    print("Матриця", name + ":")
    for row in M:
        for value in row:
            print("{:4d}".format(value), end=" ")
        print()


n = int(input("Кількість рядків матриці G: "))
m = int(input("Кількість стовпців G (рядків H): "))
p = int(input("Кількість стовпців H: "))

if n > 0 and m > 0 and p > 0:
    # розміри матриць: G(n*m), H(m*p), T(n*p)
    G = [[randint(0, 5) for j in range(m)] for i in range(n)]
    H = [[randint(0, 5) for j in range(p)] for i in range(m)]
    T = [[randint(0, 5) for j in range(p)] for i in range(n)]
    show("G", G)
    show("H", H)
    show("T", T)

    # R = G*H - 2T
    R = [[0] * p for i in range(n)]
    for i in range(n):
        for j in range(p):
            s = 0
            for k in range(m):
                s = s + G[i][k] * H[k][j]
            R[i][j] = s - 2 * T[i][j]

    show("R = G*H - 2T", R)
else:
    print("Розміри матриць мають бути додатними")
